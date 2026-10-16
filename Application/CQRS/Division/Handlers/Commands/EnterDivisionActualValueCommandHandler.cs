using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Division.Handlers.Commands;

public class EnterDivisionActualValueCommandHandler : IRequestHandler<EnterDivisionActualValueCommandRequest, Response<DivisionActualValueDto>>
{
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly ICurrentUserService currentUserService;
    private readonly ISpmsJobScheduler jobScheduler;
    private readonly IAuditContext auditContext;

    public EnterDivisionActualValueCommandHandler(
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IKpiRepository _kpiRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        ICurrentUserService _currentUserService,
        ISpmsJobScheduler _jobScheduler,
        IAuditContext _auditContext)
    {
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
        kpiRepository = _kpiRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        currentUserService = _currentUserService;
        jobScheduler = _jobScheduler;
        auditContext = _auditContext;
    }

    public async Task<Response<DivisionActualValueDto>> Handle(EnterDivisionActualValueCommandRequest request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        if (dto == null)
        {
            throw new BadRequestException("Actual value data is required.");
        }

        if (currentUserService.DivisionId.HasValue && currentUserService.DivisionId.Value != dto.DivisionId)
        {
            throw new ForbiddenException("A division account can only enter actual values for its own division.");
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

        var divisionPlan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(dto.DivisionId, kpi.ScorecardId);
        if (divisionPlan == null)
        {
            throw new BadRequestException("No division plan exists for this division and scorecard.");
        }

        // FR-906: never trust the division id in the payload. The KPI must actually
        // belong to a division goal inside the resolved division's plan, otherwise a
        // division account could write actuals for a sibling division that happens to
        // share the same corporate scorecard.
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

        var entity = new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = dto.Value,
            DataSource = dto.DataSource,
            AsOfDate = DateTime.UtcNow,
            RecordedBy = currentUserService.UserId,
            Note = dto.Note
        };

        // FR-703: when writing to a closed/locked period the LockAuthorizationBehavior
        // has already required an override reason; carry it into the append-only audit
        // record so the correction is fully attributable.
        if (!string.IsNullOrWhiteSpace(dto.OverrideReason))
        {
            auditContext.Reason = dto.OverrideReason.Trim();
        }

        await kpiActualValueRepository.Add(entity);

        jobScheduler.Enqueue<IDivisionScoreRecalculationJob>(
            job => job.RecalculateDivisionGoalAsync(kpi.DivisionGoalId.Value, period.Id));

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
            Note = entity.Note
        };

        return new Response<DivisionActualValueDto>
        {
            Success = true,
            Message = "Division actual value entered successfully.",
            Data = result
        };
    }
}
