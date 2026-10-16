using Application.DTOS.Initiative;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    public class UpdateInitiativeCommandRequest : IRequest<Response<InitiativeDto>>
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public Guid? OwnerId { get; set; }

        /// <summary>Set explicitly to change the link; omitted leaves the current link untouched.</summary>
        public Guid? GoalId { get; set; }

        public Guid? KraId { get; set; }

        public Guid? KpiId { get; set; }

        /// <summary>
        /// Explicitly removes all strategic links. Needed because a null Guid? cannot be told
        /// apart from an omitted field, and the FRD treats the links as optional.
        /// </summary>
        public bool ClearStrategicLinks { get; set; }

        /// <summary>
        /// Set by frontends that render every strategic select: the three ids below are then an
        /// authoritative snapshot (empty selects arrive as null and clear that link), rather than a
        /// merge with the links currently on the initiative.
        /// </summary>
        public bool StrategicLinksSpecified { get; set; }

        public InitiativePriority? Priority { get; set; }

        public InitiativeStatus? Status { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public decimal? Budget { get; set; }

        /// <summary>FR-404 actual expenditure.</summary>
        public decimal? ActualExpenditure { get; set; }

        public string? Resources { get; set; }
    }
}
