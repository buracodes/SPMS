using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Queries
{
    public class GetActualValueHistoryQueryHandler : IRequestHandler<GetActualValueHistoryQueryRequest, Response<ActualValueHistoryDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;
        private readonly ICurrentUserService currentUserService;

        public GetActualValueHistoryQueryHandler(
            IKpiRepository _kpiRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiActualValueRepository _kpiActualValueRepository,
            ICurrentUserService _currentUserService)
        {
            kpiRepository = _kpiRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
            currentUserService = _currentUserService;
        }

        public async Task<Response<ActualValueHistoryDto>> Handle(GetActualValueHistoryQueryRequest request, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetById(request.KpiId);
            if (kpi == null)
            {
                throw new NotFoundException(nameof(Kpi), request.KpiId);
            }

            KpiActualReadGuard.EnsureReadAllowed(kpi, currentUserService);

            var period = await fiscalPeriodRepository.GetById(request.FiscalPeriodId);
            if (period == null)
            {
                throw new NotFoundException(nameof(FiscalPeriod), request.FiscalPeriodId);
            }

            var values = await kpiActualValueRepository.GetByKpiAndPeriodAsync(request.KpiId, request.FiscalPeriodId);

            var result = new ActualValueHistoryDto
            {
                KpiId = request.KpiId,
                FiscalPeriodId = request.FiscalPeriodId,
                FiscalMonth = period.FiscalMonth
            };

            foreach (var value in values)
            {
                result.Revisions.Add(new ActualValueDto
                {
                    Id = value.Id,
                    KpiId = value.KpiId,
                    FiscalPeriodId = value.FiscalPeriodId,
                    FiscalMonth = period.FiscalMonth,
                    Quarter = period.Quarter,
                    Value = value.Value,
                    DataSource = value.DataSource,
                    AsOfDate = value.AsOfDate,
                    Note = value.Note
                });
            }

            return new Response<ActualValueHistoryDto>
            {
                Success = true,
                Message = "Actual value history retrieved successfully.",
                Data = result
            };
        }
    }
}
