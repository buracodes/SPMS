using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Queries
{
    public class GetDivisionsQueryRequest : IRequest<Response<List<DivisionDto>>>
    {
    }
}
