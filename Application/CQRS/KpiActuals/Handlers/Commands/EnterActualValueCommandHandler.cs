using Application.CQRS.KpiActuals.Requests.Commands;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Responses;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Commands
{
    public class EnterActualValueCommandHandler : IRequestHandler<EnterActualValueCommandRequest, Response<ActualValueDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;
        private readonly IUserRepository userRepository;
        private readonly ICurrentUserService currentUserService;
        private readonly ISpmsJobScheduler jobScheduler;
        private readonly IAuditContext auditContext;
        private readonly IEffectivePermissionService effectivePermissionService;
        private readonly IUnitOfWork unitOfWork;

        public EnterActualValueCommandHandler(
            IKpiRepository _kpiRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiActualValueRepository _kpiActualValueRepository,
            IUserRepository _userRepository,
            ICurrentUserService _currentUserService,
            ISpmsJobScheduler _jobScheduler,
            IAuditContext _auditContext,
            IEffectivePermissionService _effectivePermissionService,
            IUnitOfWork _unitOfWork)
        {
            kpiRepository = _kpiRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
            userRepository = _userRepository;
            currentUserService = _currentUserService;
            jobScheduler = _jobScheduler;
            auditContext = _auditContext;
            effectivePermissionService = _effectivePermissionService;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<ActualValueDto>> Handle(EnterActualValueCommandRequest request, CancellationToken cancellationToken)
        {
            var dto = request.Dto;
            if (dto == null)
            {
                throw new BadRequestException("Actual value data is required.");
            }

            var kpi = await kpiRepository.GetByIdWithScorecardAsync(dto.KpiId);
            if (kpi == null)
            {
                throw new NotFoundException(nameof(Kpi), dto.KpiId);
            }

            // Eligibility precedes every ownership/lock rule: an inactive KPI is excluded
            // from scoring, so accepting an actual for it would silently disappear.
            if (!kpi.IsActive)
            {
                throw new BadRequestException("This KPI is inactive and cannot accept actual values.");
            }

            await KpiActualWriteGuard.EnsureWriteAllowedAsync(
                kpi, currentUserService, effectivePermissionService, cancellationToken);

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
                    // Append-only correction: retire the old revision first so the filtered
                    // unique index never sees two current rows. The replacement already points
                    // back to it, so the FK can be satisfied without a third save.
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

            jobScheduler.Enqueue<IScoreSnapshotRecalculationJob>(
                job => job.RecalculateKpiCascadeAsync(kpi.Id, period.Id));

            var enteredByNames = ActualAttributionNames.Resolve(userRepository, new[] { entity.RecordedBy });

            var result = new ActualValueDto
            {
                Id = entity.Id,
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

            return new Response<ActualValueDto>
            {
                Success = true,
                Message = "Actual value entered successfully.",
                Data = result
            };
        }
    }
}
