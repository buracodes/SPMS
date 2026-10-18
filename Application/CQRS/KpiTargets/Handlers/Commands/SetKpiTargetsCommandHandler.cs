using Application.CQRS.KpiTargets.Requests.Commands;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.KpiTargets.Handlers.Commands
{
    public class SetKpiTargetsCommandHandler : IRequestHandler<SetKpiTargetsCommandRequest, Response<GetKpiTargetsDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IFiscalCalendarService fiscalCalendarService;
        private readonly ISpmsJobScheduler jobScheduler;
        private readonly IUnitOfWork unitOfWork;

        public SetKpiTargetsCommandHandler(
            IKpiRepository _kpiRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IFiscalCalendarService _fiscalCalendarService,
            ISpmsJobScheduler _jobScheduler,
            IUnitOfWork _unitOfWork)
        {
            kpiRepository = _kpiRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
            fiscalCalendarService = _fiscalCalendarService;
            jobScheduler = _jobScheduler;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<GetKpiTargetsDto>> Handle(SetKpiTargetsCommandRequest request, CancellationToken cancellationToken)
        {
            var dto = request.Dto;
            if (dto == null)
            {
                throw new BadRequestException("Target data is required.");
            }

            var kpi = await kpiRepository.GetByIdWithScorecardAsync(dto.KpiId);
            if (kpi == null)
            {
                throw new NotFoundException(nameof(Kpi), dto.KpiId);
            }

            var fiscalYear = await fiscalYearRepository.GetById(dto.FiscalYearId);
            if (fiscalYear == null)
            {
                throw new NotFoundException(nameof(FiscalYear), dto.FiscalYearId);
            }

            if (kpi.Scorecard != null && kpi.Scorecard.FiscalYearId != fiscalYear.Id)
            {
                throw new BadRequestException("The KPI does not belong to the selected fiscal year's scorecard.");
            }

            var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(dto.FiscalYearId);

            if (periods.Count != 12)
            {
                // Stage the regenerated periods with the rest of the write so the calendar and
                // the targets commit together (or not at all) in a single transaction.
                var generated = fiscalCalendarService.GeneratePeriods(fiscalYear);
                await unitOfWork.AddRangeAsync(generated, cancellationToken);
                periods = generated;
            }

            var periodByMonth = periods.ToDictionary(p => p.FiscalMonth);
            var periodById = periods.ToDictionary(p => p.Id);

            for (var quarter = 1; quarter <= 4; quarter++)
            {
                if (!periodByMonth[quarter * 3].IsQuarterEnd)
                {
                    throw new BadRequestException($"Q{quarter} targets must use a quarter-end fiscal period.");
                }
            }
            if (!periodByMonth[12].IsYearEnd)
            {
                throw new BadRequestException("The annual target must use the fiscal year-end period.");
            }

            var annualTarget = dto.AnnualTarget!.Value;

            // FR-304 defaults: the very first target configuration locks the data-range
            // defaults - Baseline = Minimum, Annual Target = Maximum - unless they were
            // already set explicitly when the KPI was created.
            var existingTargets = await kpiTargetRepository.GetByKpiAndFiscalYearAsync(kpi.Id, fiscalYear.Id);
            if (existingTargets.Count == 0)
            {
                if (kpi.Baseline is null && kpi.MinValue.HasValue)
                {
                    kpi.Baseline = kpi.MinValue;
                }
                if (kpi.MaxValue is null)
                {
                    kpi.MaxValue = annualTarget;
                }
            }

            var monthlyByMonth = new Dictionary<int, decimal>();
            for (var month = 1; month <= 12; month++)
            {
                monthlyByMonth[month] = dto.MonthlyTargets[month - 1];
            }

            IReadOnlyDictionary<int, decimal> quarterlyByQuarter;
            if (dto.QuarterlyTargets is { Count: 4 } supplied)
            {
                quarterlyByQuarter = Enumerable.Range(1, 4).ToDictionary(q => q, q => supplied[q - 1]);
            }
            else
            {
                // FR-304: quarterly targets follow from the monthly targets when the
                // quarter rows are not explicitly configured.
                quarterlyByQuarter = KpiTargetReconciliation.DeriveQuarterly(kpi.IsCumulative, monthlyByMonth, annualTarget);
            }

            KpiTargetReconciliation.Validate(kpi.IsCumulative, monthlyByMonth, quarterlyByQuarter, annualTarget);

            var targets = new List<KpiTarget>();

            for (var month = 1; month <= 12; month++)
            {
                targets.Add(new KpiTarget
                {
                    Id = Guid.NewGuid(),
                    KpiId = kpi.Id,
                    FiscalPeriodId = periodByMonth[month].Id,
                    TargetValue = monthlyByMonth[month],
                    Granularity = TargetGranularity.Monthly
                });
            }

            for (var quarter = 1; quarter <= 4; quarter++)
            {
                targets.Add(new KpiTarget
                {
                    Id = Guid.NewGuid(),
                    KpiId = kpi.Id,
                    FiscalPeriodId = periodByMonth[quarter * 3].Id,
                    TargetValue = quarterlyByQuarter[quarter],
                    Granularity = TargetGranularity.Quarterly
                });
            }

            targets.Add(new KpiTarget
            {
                Id = Guid.NewGuid(),
                KpiId = kpi.Id,
                FiscalPeriodId = periodByMonth[12].Id,
                TargetValue = annualTarget,
                Granularity = TargetGranularity.Annual
            });

            // ReplaceForFiscalYearAsync stages the target upsert; commit the periods (when
            // regenerated) and the targets together before scheduling recalculations.
            await kpiTargetRepository.ReplaceForFiscalYearAsync(kpi.Id, fiscalYear.Id, targets);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var period in periods.Where(p => p.FiscalMonth >= 1 && p.FiscalMonth <= 12))
            {
                jobScheduler.Enqueue<IScoreSnapshotRecalculationJob>(
                    job => job.RecalculateKpiCascadeAsync(kpi.Id, period.Id));
            }

            var result = BuildResult(kpi.Id, fiscalYear.Id, targets, periodById);

            return new Response<GetKpiTargetsDto>
            {
                Success = true,
                Message = "KPI targets saved successfully.",
                Data = result
            };
        }

        private static GetKpiTargetsDto BuildResult(
            Guid kpiId,
            Guid fiscalYearId,
            IReadOnlyList<KpiTarget> targets,
            IReadOnlyDictionary<Guid, FiscalPeriod> periodById)
        {
            var result = new GetKpiTargetsDto
            {
                KpiId = kpiId,
                FiscalYearId = fiscalYearId
            };

            foreach (var target in targets.OrderBy(t => t.Granularity).ThenBy(t => periodById[t.FiscalPeriodId].FiscalMonth))
            {
                var period = periodById[target.FiscalPeriodId];
                result.Targets.Add(new KpiTargetValueDto
                {
                    FiscalMonth = period.FiscalMonth,
                    Quarter = period.Quarter,
                    Granularity = target.Granularity,
                    TargetValue = target.TargetValue
                });
            }

            return result;
        }
    }
}
