using Application.DTOS.Goal;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Goals.Requests.Queries;

public class GetGoalByIdQueryRequest : IRequest<Response<GoalDto>>
{
    public Guid Id { get; set; }
}
