using Application.DTOS.KpiActual;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiActuals.Requests.Queries
{
    public class GetActualValuesQueryRequest : IRequest<Response<GetActualValuesDto>>
    {
        public Guid KpiId { get; set; }
        public Guid FiscalYearId { get; set; }
    }
}
