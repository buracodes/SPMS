using Application.DTOS.KpiTarget;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiTargets.Requests.Queries
{
    public class GetKpiTargetsQueryRequest : IRequest<Response<GetKpiTargetsDto>>
    {
        public Guid KpiId { get; set; }
        public Guid FiscalYearId { get; set; }
    }
}
