using Application.DTOS.Initiative;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    public class UpdateMilestoneCommandRequest : IRequest<Response<MilestoneDto>>
    {
        public Guid Id { get; set; }

        public Guid InitiativeId { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public int? CompletionPercentage { get; set; }

        public DateTime? DueDate { get; set; }

        public decimal? Weight { get; set; }

        /// <summary>Explicit status; ignored (forced to Completed) when completion reaches 100%.</summary>
        public MilestoneStatus? Status { get; set; }

        /// <summary>Progress note.</summary>
        public string? Comments { get; set; }
    }
}
