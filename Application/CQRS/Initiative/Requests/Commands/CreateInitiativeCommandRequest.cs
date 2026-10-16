using Application.DTOS.Initiative;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    public class CreateInitiativeCommandRequest : IRequest<Response<InitiativeDto>>
    {
        public required string Name { get; set; }

        public string? Description { get; set; }

        public Guid? GoalId { get; set; }

        public Guid? KraId { get; set; }

        public Guid? KpiId { get; set; }

        public Guid OwnerId { get; set; }

        public InitiativePriority Priority { get; set; } = InitiativePriority.Medium;

        public InitiativeStatus Status { get; set; } = InitiativeStatus.NotStarted;

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public decimal Budget { get; set; }

        public string? Resources { get; set; }
    }
}
