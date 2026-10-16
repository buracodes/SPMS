using MediatR;

namespace Application.CQRS.Scorecard.Requests.Commands;

public class DeleteScorecardCommandRequest : IRequest
{
    public Guid Id { get; set; }
}
