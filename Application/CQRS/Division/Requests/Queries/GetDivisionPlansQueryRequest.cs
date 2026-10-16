using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Queries
{
    public class GetDivisionPlansQueryRequest : IRequest<Response<List<DivisionPlanSummaryDto>>>
    {
        public Guid DivisionId { get; set; }
    }
}
