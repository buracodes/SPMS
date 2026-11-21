using Application.DTOS.KpiActual;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiActuals.Requests.Queries
{
    public class GetActualValueHistoryQueryRequest : IRequest<Response<ActualValueHistoryDto>>
    {
        public Guid ScorecardId { get; set; }
        public Guid KpiId { get; set; }
        public Guid FiscalPeriodId { get; set; }
    }
}
