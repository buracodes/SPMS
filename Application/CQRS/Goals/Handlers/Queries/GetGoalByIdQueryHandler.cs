using Application.CQRS.Goals.Requests.Queries;
using Application.DTOS.Goal;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Goals.Handlers.Queries;

public class GetGoalByIdQueryHandler : IRequestHandler<GetGoalByIdQueryRequest, Response<GoalDto>>
{
    private readonly IGoalRepository goalRepository;

    public GetGoalByIdQueryHandler(IGoalRepository _goalRepository)
    {
        goalRepository = _goalRepository;
    }

    public async Task<Response<GoalDto>> Handle(GetGoalByIdQueryRequest request, CancellationToken cancellationToken)
    {
        var goal = await goalRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(Domain.Entities.Goal), request.Id);

        return new Response<GoalDto>
        {
            Success = true,
            Message = "Goal retrieved successfully.",
            Data = new GoalDto
            {
                Id = goal.Id,
                Name = goal.Name,
                Description = goal.Description,
                CreatedBy = goal.CreatedBy,
                CreatedDate = goal.CreatedDate,
                UpdatedBy = goal.UpdatedBy,
                UpdatedDate = goal.UpdatedDate,
                Status = goal.Status
            }
        };
    }
}
