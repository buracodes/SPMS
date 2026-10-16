using MediatR;

namespace Application.CQRS.Goals.Requests.Commands;

public class DeleteGoalCommandRequest : IRequest
{
    public Guid Id { get; set; }
}
