using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class SetDivisionContributionsCommandRequest : IRequest<Response<DivisionPlanDto>>
    {
        public Guid DivisionId { get; set; }
        public Guid ScorecardId { get; set; }
        public List<Guid> GoalIds { get; set; } = new();

        /// <summary>
        /// FR-902: when a deselected corporate goal still has division goals (and their
        /// KPIs) the caller offers keep-or-delete. Set to true to soft-delete the dependent
        /// goals/KPIs; when false the contribution is removed and the dependents are kept.
        /// The frontend always asks the user before sending either value.
        /// </summary>
        public bool CascadeRemoveDependents { get; set; }
    }
}
