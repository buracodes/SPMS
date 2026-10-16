using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class CreateDivisionGoalCommandRequest : IRequest<Response<DivisionGoalDto>>
    {
        public Guid DivisionId { get; set; }
        public Guid ScorecardId { get; set; }
        public Guid ParentGoalId { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public decimal Weight { get; set; }
        public string? ContributionDescription { get; set; }
    }
}
