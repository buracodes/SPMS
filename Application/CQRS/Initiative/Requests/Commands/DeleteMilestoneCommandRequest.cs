using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    public class DeleteMilestoneCommandRequest : IRequest
    {
        public required Guid Id { get; set; }

        public Guid InitiativeId { get; set; }
    }
}