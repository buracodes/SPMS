using Application.CQRS.KpiActuals.Requests.Commands;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Commands
{
    public class EnterActualValueCommandHandler : IRequestHandler<EnterActualValueCommandRequest, Response<ActualValueDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;
        private readonly ICurrentUserService currentUserService;
        private readonly ISpmsJobScheduler jobScheduler;
        private readonly IAuditContext auditContext;

        public EnterActualValueCommandHandler(
            IKpiRepository _kpiRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiActualValueRepository _kpiActualValueRepository,
            ICurrentUserService _currentUserService,
            ISpmsJobScheduler _jobScheduler,
            IAuditContext _auditContext)
        {
            kpiRepository = _kpiRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
            currentUserService = _currentUserService;
            jobScheduler = _jobScheduler;
            auditContext = _auditContext;
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

            jobScheduler.Enqueue<IScoreSnapshotRecalculationJob>(
                job => job.RecalculateKpiCascadeAsync(kpi.Id, period.Id));

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
                Note = entity.Note
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
