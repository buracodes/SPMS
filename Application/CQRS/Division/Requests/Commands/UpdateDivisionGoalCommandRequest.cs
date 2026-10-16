using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class UpdateDivisionGoalCommandRequest : IRequest<Response<DivisionGoalDto>>
    {
        public Guid DivisionGoalId { get; set; }
        public Guid? ParentGoalId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public decimal? Weight { get; set; }
        public string? ContributionDescription { get; set; }
    }
}
