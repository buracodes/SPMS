using Application.DTOS.Division;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Requests.Queries
{
    public class GetDivisionPlanQueryRequest : IRequest<Response<DivisionPlanDto>>
    {
        public Guid DivisionId { get; set; }
        public Guid ScorecardId { get; set; }

        public PeriodType? PeriodType { get; set; }
        public int? Quarter { get; set; }
        public int? FromMonth { get; set; }
        public int? ToMonth { get; set; }
        public int? Year { get; set; }
    }
}
