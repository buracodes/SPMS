using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Queries
{
    public class GetActualValuesQueryHandler : IRequestHandler<GetActualValuesQueryRequest, Response<GetActualValuesDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;

        public GetActualValuesQueryHandler(
            IKpiRepository _kpiRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiActualValueRepository _kpiActualValueRepository)
        {
            kpiRepository = _kpiRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
        }

        public async Task<Response<GetActualValuesDto>> Handle(GetActualValuesQueryRequest request, CancellationToken cancellationToken)
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

            var values = await kpiActualValueRepository.GetForFiscalYearAsync(request.KpiId, request.FiscalYearId);

            // Latest write wins per fiscal period per data source.
            var latestByPeriodAndSource = values
                .GroupBy(v => new { v.FiscalPeriodId, v.DataSource })
                .Select(g => g.OrderByDescending(v => v.AsOfDate).First())
                .OrderBy(v => periodById.GetValueOrDefault(v.FiscalPeriodId)?.FiscalMonth)
                .ThenBy(v => v.DataSource)
                .ToList();

            var result = new GetActualValuesDto
            {
                KpiId = request.KpiId,
                FiscalYearId = request.FiscalYearId
            };

            foreach (var value in latestByPeriodAndSource)
            {
                var period = periodById.GetValueOrDefault(value.FiscalPeriodId);
                result.Values.Add(new ActualValueDto
                {
                    Id = value.Id,
                    KpiId = value.KpiId,
                    FiscalPeriodId = value.FiscalPeriodId,
                    FiscalMonth = period?.FiscalMonth ?? 0,
                    Quarter = period?.Quarter ?? 0,
                    Value = value.Value,
                    DataSource = value.DataSource,
                    AsOfDate = value.AsOfDate,
                    Note = value.Note
                });
            }

            return new Response<GetActualValuesDto>
            {
                Success = true,
                Message = "Actual values retrieved successfully.",
                Data = result
            };
        }
    }
}
