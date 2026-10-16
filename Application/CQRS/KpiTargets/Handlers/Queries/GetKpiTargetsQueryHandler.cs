using Application.CQRS.KpiTargets.Requests.Queries;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiTargets.Handlers.Queries
{
    public class GetKpiTargetsQueryHandler : IRequestHandler<GetKpiTargetsQueryRequest, Response<GetKpiTargetsDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;

        public GetKpiTargetsQueryHandler(
            IKpiRepository _kpiRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository)
        {
            kpiRepository = _kpiRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
        }

        public async Task<Response<GetKpiTargetsDto>> Handle(GetKpiTargetsQueryRequest request, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetById(request.KpiId);
            if (kpi == null)
            {
                throw new NotFoundException(nameof(Kpi), request.KpiId);
            }

            var fiscalYear = await fiscalYearRepository.GetById(request.FiscalYearId);
            if (fiscalYear == null)
            {
                throw new NotFoundException(nameof(FiscalYear), request.FiscalYearId);
            }

            var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(request.FiscalYearId);
            var periodById = periods.ToDictionary(p => p.Id);

            var targets = await kpiTargetRepository.GetByKpiAndFiscalYearAsync(request.KpiId, request.FiscalYearId);

            var result = new GetKpiTargetsDto
            {
                KpiId = request.KpiId,
                FiscalYearId = request.FiscalYearId
            };

            foreach (var target in targets.OrderBy(t => t.Granularity).ThenBy(t => periodById.GetValueOrDefault(t.FiscalPeriodId)?.FiscalMonth))
            {
                var period = periodById.GetValueOrDefault(target.FiscalPeriodId);
                result.Targets.Add(new KpiTargetValueDto
                {
                    FiscalMonth = period?.FiscalMonth ?? 0,
                    Quarter = period?.Quarter ?? 0,
                    Granularity = target.Granularity,
                    TargetValue = target.TargetValue
                });
            }

            return new Response<GetKpiTargetsDto>
            {
                Success = true,
                Message = "KPI targets retrieved successfully.",
                Data = result
            };
        }
    }
}
