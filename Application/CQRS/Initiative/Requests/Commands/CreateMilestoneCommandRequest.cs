using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    public class CreateMilestoneCommandRequest : IRequest<Response<MilestoneDto>>
    {
        public required string Name { get; set; }

        public string? Description { get; set; }

        public int CompletionPercentage { get; set; }

        public DateTime DueDate { get; set; }

        public decimal Weight { get; set; } = 1m;

        /// <summary>Explicit status; ignored (forced to Completed) when completion is 100%.</summary>
        public MilestoneStatus? Status { get; set; }

        public string? Comments { get; set; }

        public Guid InitiativeId { get; set; }
    }
}
