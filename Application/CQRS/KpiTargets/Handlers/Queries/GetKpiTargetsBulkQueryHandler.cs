using Application.CQRS.KpiTargets.Requests.Queries;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiTargets.Handlers.Queries
{
    /// <summary>
    /// Resolves the KPIs of the requested scope and returns their targets in one payload.
    /// The scope is a fiscal year, optionally narrowed to a single scorecard, which is what
    /// the Targets page filters on; goal and KRA narrowing happens on the rows already loaded.
    /// </summary>
    public class GetKpiTargetsBulkQueryHandler
        : IRequestHandler<GetKpiTargetsBulkQueryRequest, Response<GetKpiTargetsBulkDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;

        public GetKpiTargetsBulkQueryHandler(
            IKpiRepository _kpiRepository,
            IScorecardRepository _scorecardRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository)
        {
            kpiRepository = _kpiRepository;
            scorecardRepository = _scorecardRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
        }

        public async Task<Response<GetKpiTargetsBulkDto>> Handle(
            GetKpiTargetsBulkQueryRequest request,
            CancellationToken cancellationToken)
        {
            var fiscalYear = await fiscalYearRepository.GetById(request.FiscalYearId);
            if (fiscalYear == null)
            {
                throw new NotFoundException(nameof(FiscalYear), request.FiscalYearId);
            }

            var scorecards = await scorecardRepository.GetAll();
            if (request.ScorecardId.HasValue && scorecards.All(s => s.Id != request.ScorecardId.Value))
            {
                throw new NotFoundException(nameof(Scorecard), request.ScorecardId.Value);
            }

            var scopedScorecardIds = scorecards
                .Where(s => s.FiscalYearId == request.FiscalYearId &&
                            (!request.ScorecardId.HasValue || s.Id == request.ScorecardId.Value))
                .Select(s => s.Id)
                .ToHashSet();

            if (scopedScorecardIds.Count == 0)
            {
                return EmptyResult(request);
            }

            var kpis = (await kpiRepository.GetAllKpisWithDetailsAsync())
                .Where(k => scopedScorecardIds.Contains(k.ScorecardId))
                .OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(request.FiscalYearId);
            var monthByPeriodId = periods.ToDictionary(p => p.Id, p => p.FiscalMonth);

            var result = new GetKpiTargetsBulkDto
            {
                FiscalYearId = request.FiscalYearId,
                ScorecardId = request.ScorecardId
            };

            foreach (var scorecardId in scopedScorecardIds)
            {
                var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecardId, request.FiscalYearId);
                var targetsByKpi = targets
                    .GroupBy(t => t.KpiId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var kpi in kpis.Where(k => k.ScorecardId == scorecardId))
                {
                    var bundle = new KpiTargetsBundleDto
                    {
                        KpiId = kpi.Id,
                        FiscalYearId = request.FiscalYearId
                    };

                    if (targetsByKpi.TryGetValue(kpi.Id, out var kpiTargets))
                    {
                        foreach (var target in kpiTargets.OrderBy(t => t.Granularity))
                        {
                            bundle.Targets.Add(new KpiTargetValueDto
                            {
                                FiscalMonth = monthByPeriodId.GetValueOrDefault(target.FiscalPeriodId),
                                Quarter = QuarterOf(monthByPeriodId.GetValueOrDefault(target.FiscalPeriodId)),
                                Granularity = target.Granularity,
                                TargetValue = target.TargetValue
                            });
                        }
                    }

                    result.Kpis.Add(bundle);
                }
            }

            return new Response<GetKpiTargetsBulkDto>
            {
                Success = true,
                Message = "KPI targets retrieved successfully.",
                Data = result
            };
        }

        private static Response<GetKpiTargetsBulkDto> EmptyResult(GetKpiTargetsBulkQueryRequest request)
        {
            return new Response<GetKpiTargetsBulkDto>
            {
                Success = true,
                Message = "KPI targets retrieved successfully.",
                Data = new GetKpiTargetsBulkDto
                {
                    FiscalYearId = request.FiscalYearId,
                    ScorecardId = request.ScorecardId
                }
            };
        }

        private static int QuarterOf(int fiscalMonth)
        {
            if (fiscalMonth < 1 || fiscalMonth > 12) return 0;
            return ((fiscalMonth - 1) / 3) + 1;
        }
    }
}
