using Application.DTOS.Scorecard;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Scorecard.Requests.Commands;

public class CloseScorecardCommandRequest : IRequest<Response<ScorecardDto>>
{
    public Guid Id { get; set; }
}
