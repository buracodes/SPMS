using Application.DTOS.Initiative;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    /// <summary>
    /// FR-404 actual expenditure for a single initiative, deliberately separate from
    /// <see cref="UpdateInitiativeCommandRequest"/>: that request carries every initiative detail
    /// (name, owner, dates, budget plan, strategic links) behind the <c>initiatives:edit</c>
    /// policy, while this one carries nothing but the spend figure. The endpoint is reachable by
    /// the assigned owner through <c>InitiativeGuard.EnsureCanUpdateExpenditure</c>, so an owner can
    /// post spend against their own initiative without being able to touch the fields that policy
    /// protects.
    /// </summary>
    public class UpdateInitiativeExpenditureCommandRequest : IRequest<Response<InitiativeDto>>
    {
        public Guid Id { get; set; }

        /// <summary>The revised spend to date. Replaces the recorded figure rather than adding to it.</summary>
        public decimal ActualExpenditure { get; set; }
    }
}
