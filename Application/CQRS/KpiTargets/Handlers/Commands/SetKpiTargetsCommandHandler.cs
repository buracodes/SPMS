using Application.CQRS.KpiTargets.Requests.Commands;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Responses;
using Domain.Entities;
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

        public SetKpiTargetsCommandHandler(
            IKpiRepository _kpiRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IFiscalCalendarService _fiscalCalendarService,
            ISpmsJobScheduler _jobScheduler)
        {
            kpiRepository = _kpiRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
            fiscalCalendarService = _fiscalCalendarService;
            jobScheduler = _jobScheduler;
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
                var generated = fiscalCalendarService.GeneratePeriods(fiscalYear);
                await fiscalPeriodRepository.AddRange(generated);
                periods = generated;
            }

            var periodByMonth = periods.ToDictionary(p => p.FiscalMonth);
            var periodById = periods.ToDictionary(p => p.Id);
            var targets = new List<KpiTarget>();

            for (var month = 1; month <= 12; month++)
            {
                var monthlyValue = dto.MonthlyTargets.ElementAtOrDefault(month - 1);
                targets.Add(new KpiTarget
                {
                    Id = Guid.NewGuid(),
                    KpiId = kpi.Id,
                    FiscalPeriodId = periodByMonth[month].Id,
                    TargetValue = monthlyValue,
                    Granularity = TargetGranularity.Monthly
                });
            }

            if (dto.QuarterlyTargets != null)
            {
                for (var quarter = 1; quarter <= 4; quarter++)
                {
                    var quarterlyValue = dto.QuarterlyTargets.ElementAtOrDefault(quarter - 1);
                    targets.Add(new KpiTarget
                    {
                        Id = Guid.NewGuid(),
                        KpiId = kpi.Id,
                        FiscalPeriodId = periodByMonth[quarter * 3].Id,
                        TargetValue = quarterlyValue,
                        Granularity = TargetGranularity.Quarterly
                    });
                }
            }

            if (dto.AnnualTarget.HasValue)
            {
                targets.Add(new KpiTarget
                {
                    Id = Guid.NewGuid(),
                    KpiId = kpi.Id,
                    FiscalPeriodId = periodByMonth[12].Id,
                    TargetValue = dto.AnnualTarget.Value,
                    Granularity = TargetGranularity.Annual
                });
            }

            await kpiTargetRepository.ReplaceForFiscalYearAsync(kpi.Id, fiscalYear.Id, targets);

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
