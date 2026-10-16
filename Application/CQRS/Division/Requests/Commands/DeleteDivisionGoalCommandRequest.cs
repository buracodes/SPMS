using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class DeleteDivisionGoalCommandRequest : IRequest<bool>
    {
        public Guid DivisionGoalId { get; set; }
    }
}
