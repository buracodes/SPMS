using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Responses;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using System.Linq;

namespace Application.CQRS.Division.Handlers.Commands;

public class EnterDivisionActualValueCommandHandler : IRequestHandler<EnterDivisionActualValueCommandRequest, Response<DivisionActualValueDto>>
{
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IUserRepository userRepository;
    private readonly ICurrentUserService currentUserService;
    private readonly IEffectivePermissionService effectivePermissionService;
    private readonly ISpmsJobScheduler jobScheduler;
    private readonly IAuditContext auditContext;
    private readonly IUnitOfWork unitOfWork;

    public EnterDivisionActualValueCommandHandler(
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IKpiRepository _kpiRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IUserRepository _userRepository,
        ICurrentUserService _currentUserService,
        IEffectivePermissionService _effectivePermissionService,
        ISpmsJobScheduler _jobScheduler,
        IAuditContext _auditContext,
        IUnitOfWork _unitOfWork)
    {
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
        kpiRepository = _kpiRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        userRepository = _userRepository;
        currentUserService = _currentUserService;
        effectivePermissionService = _effectivePermissionService;
        jobScheduler = _jobScheduler;
        auditContext = _auditContext;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<DivisionActualValueDto>> Handle(EnterDivisionActualValueCommandRequest request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        if (dto == null)
        {
            throw new BadRequestException("Actual value data is required.");
        }

        // The authorization scope is the whole set of divisions the caller may access, not
        // the payload. Explicitly authorized organization-wide entry (the live
        // actuals:override grant held by Admin and Power User) is evaluated before the
        // ordinary division-account restriction. A corporate caller (empty scope) is still
        // fenced by the plan-member check below; a division caller may only target one of its
        // authorized divisions unless it holds that right.
        var divisionScope = currentUserService.DivisionIds;
        if (divisionScope.Count > 0
            && !divisionScope.Contains(dto.DivisionId)
            && !await HasCrossDivisionOverrideAsync(cancellationToken))
        {
            throw new ForbiddenException("A division account can only enter actual values for its authorized divisions.");
        }

        var division = await divisionRepository.GetById(dto.DivisionId);
        if (division == null)
        {
            throw new NotFoundException(nameof(Domain.Entities.Division), dto.DivisionId);
        }

        var kpi = await kpiRepository.GetByIdWithScorecardAsync(dto.KpiId);
        if (kpi == null || kpi.DivisionGoalId == null)
        {
            throw new BadRequestException("The specified KPI is not a division KPI.");
        }

        // Eligibility precedes every ownership/lock rule: an inactive KPI is excluded
        // from scoring, so accepting an actual for it would silently disappear.
        if (!kpi.IsActive)
        {
            throw new BadRequestException("This KPI is inactive and cannot accept actual values.");
        }

        var divisionPlan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(dto.DivisionId, kpi.ScorecardId);
        if (divisionPlan == null)
        {
            throw new BadRequestException("No division plan exists for this division and scorecard.");
        }

        // FR-906: division performance entry opens only after the plan is finalized. The
        // finalization step is the plan-integrity checkpoint (weights, KPIs and targets),
        // so accepting actuals for a draft would score a plan that may still change shape.
        // Period-level FR-308 windows remain a separate gate checked by the period lock.
        if (divisionPlan.PlanStatus != DivisionPlanStatus.Finalized)
        {
            throw new BadRequestException(
                "The division plan must be finalized before actual values can be entered.");
        }

        // Never trust the division id in the payload. The KPI must actually belong to a
        // division goal inside the resolved division's plan, otherwise a division account
        // could write actuals for a sibling division that happens to share the same
        // corporate scorecard.
        var divisionGoal = await divisionGoalRepository.GetById(kpi.DivisionGoalId.Value);
        if (divisionGoal == null || divisionGoal.DivisionPlanId != divisionPlan.Id)
        {
            throw new ForbiddenException("The KPI does not belong to a division goal in this division's plan.");
        }

        var period = await fiscalPeriodRepository.GetById(dto.FiscalPeriodId);
        if (period == null)
        {
            throw new NotFoundException(nameof(FiscalPeriod), dto.FiscalPeriodId);
        }

        if (kpi.Scorecard != null && kpi.Scorecard.FiscalYearId != period.FiscalYearId)
        {
            throw new BadRequestException("The fiscal period does not belong to the KPI's fiscal year.");
        }

        // Every HTTP entry is a manual correction. DataSource is never taken from the
        // request body, so a client-supplied "dataSource" cannot create an Automated row.
        var entity = new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = dto.Value,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow,
            RecordedBy = currentUserService.UserId,
            Note = dto.Note,
            IsCurrent = true
        };

        // FR-703: when writing to a closed/locked period the LockAuthorizationBehavior
        // has already required an override reason; carry it into the append-only audit
        // record so the correction is fully attributable. Override is authorization,
        // never a data source.
        var overrideReason = string.IsNullOrWhiteSpace(dto.OverrideReason)
            ? null
            : dto.OverrideReason.Trim();

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var previous = await kpiActualValueRepository.GetCurrentAsync(
                kpi.Id, period.Id, cancellationToken);

            if (previous is not null)
            {
                // Retire first so the filtered unique index never sees two current rows.
                // The replacement already points back to it, so no third save is needed.
                previous.IsCurrent = false;
                entity.SupersedesActualId = previous.Id;

                if (overrideReason is not null)
                {
                    auditContext.Reason = overrideReason;
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // The interceptor clears Reason after each save; re-apply it so the Create
            // audit row for the replacement carries the same override reason.
            if (overrideReason is not null)
            {
                auditContext.Reason = overrideReason;
            }

            await kpiActualValueRepository.AddWithoutSave(entity);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }, cancellationToken);

        jobScheduler.Enqueue<IDivisionScoreRecalculationJob>(
            job => job.RecalculateDivisionGoalAsync(kpi.DivisionGoalId.Value, period.Id));

        var enteredByNames = ActualAttributionNames.Resolve(userRepository, new[] { entity.RecordedBy });

        var result = new DivisionActualValueDto
        {
            Id = entity.Id,
            DivisionId = dto.DivisionId,
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            FiscalMonth = period.FiscalMonth,
            Quarter = period.Quarter,
            Value = entity.Value,
            DataSource = entity.DataSource,
            AsOfDate = entity.AsOfDate,
            IsCurrent = entity.IsCurrent,
            SupersedesActualId = entity.SupersedesActualId,
            Note = entity.Note,
            RecordedBy = entity.RecordedBy,
            EnteredByName = entity.RecordedBy is Guid id && enteredByNames.TryGetValue(id, out var name) ? name : null,
            CreatedDate = entity.CreatedDate
        };

        return new Response<DivisionActualValueDto>
        {
            Success = true,
            Message = "Division actual value entered successfully.",
            Data = result
        };
    }

    // The override right is resolved live, so a revoked grant stops applying on the next
    // request instead of when the JWT happens to be refreshed.
    private async Task<bool> HasCrossDivisionOverrideAsync(CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not Guid userId || userId == Guid.Empty)
        {
            return false;
        }

        return await effectivePermissionService.HasPermissionAsync(
            userId, Permissions.ActualsOverride, cancellationToken);
    }
}
