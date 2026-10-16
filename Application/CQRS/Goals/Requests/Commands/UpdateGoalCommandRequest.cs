using Application.DTOS.Goal;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Goals.Requests.Commands;

public class UpdateGoalCommandRequest : IRequest<Response<GoalDto>>
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Weight { get; set; }
    public bool? IsActive { get; set; }
}
