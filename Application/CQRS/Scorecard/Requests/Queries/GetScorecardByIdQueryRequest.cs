using Application.DTOS.Scorecard;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Scorecard.Requests.Queries;

public class GetScorecardByIdQueryRequest : IRequest<Response<ScorecardDto>>
{
    public Guid Id { get; set; }
}
