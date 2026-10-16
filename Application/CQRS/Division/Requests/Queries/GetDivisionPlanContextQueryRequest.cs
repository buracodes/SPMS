using Application.DTOS.Division;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Requests.Queries
{
    /// <summary>
    /// Loads everything the Division Planning page needs from a division scorecard id
    /// alone (or an explicit division), including the aligned corporate goals and the
    /// period-scoped performance scores.
    /// </summary>
    public class GetDivisionPlanContextQueryRequest : IRequest<Response<DivisionPlanDto>>
    {
        public Guid ScorecardId { get; set; }
        public Guid? DivisionId { get; set; }

        public PeriodType? PeriodType { get; set; }
        public int? Quarter { get; set; }
        public int? FromMonth { get; set; }
        public int? ToMonth { get; set; }
        public int? Year { get; set; }
    }
}
