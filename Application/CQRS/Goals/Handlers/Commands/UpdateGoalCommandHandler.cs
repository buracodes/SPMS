using Application.CQRS.Goals.Requests.Commands;
using Application.DTOS.Goal;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Application.CQRS.Goals.Handlers.Commands;

public class UpdateGoalCommandHandler : IRequestHandler<UpdateGoalCommandRequest, Response<GoalDto>>
{
    private readonly IGoalRepository goalRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateGoalCommandHandler(IGoalRepository _goalRepository, IUnitOfWork _unitOfWork)
    {
        goalRepository = _goalRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<GoalDto>> Handle(UpdateGoalCommandRequest request, CancellationToken cancellationToken)
    {
        var goal = await goalRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(Domain.Entities.Goal), request.Id);

        // Weight/name edits go through a single serializable transaction (ADR-009): the
        // budget re-read (excluding this goal) and the save happen together, so two
        // concurrent edits cannot both pass the 100% check, and a lost-update against a
        // stale RowVersion rolls back instead of landing half-applied.
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var updatedName = request.Name.Trim();
                var duplicates = (await goalRepository.GetByScorecardAsync(goal.ScorecardId))
                    .Where(g => !g.IsDeleted && g.Id != goal.Id
                        && string.Equals(g.Name, updatedName, StringComparison.OrdinalIgnoreCase));
                if (duplicates.Any())
                    throw new BadRequestException($"A goal named '{updatedName}' already exists under this scorecard.");
                goal.Name = updatedName;
            }

            if (request.Description != null)
                goal.Description = request.Description;

            if (request.Weight.HasValue)
            {
                // FR-302: the stored weight of every OTHER goal is read fresh in SQL inside
                // this transaction. Assigned weights are never rewritten here - only this
                // goal's own weight moves.
                var otherGoalsWeight = await goalRepository.GetWeightsSumAsync(goal.ScorecardId, goal.Id, cancellationToken: cancellationToken);
                var newWeight = request.Weight.Value;

                if (otherGoalsWeight + newWeight > 100m)
                {
                    throw new BadRequestException($"Updating this goal would bring the total goal weight to {otherGoalsWeight + newWeight:F2}% which exceeds 100%. Adjust the weight or existing goals.");
                }

                goal.Weight = newWeight;
            }

            if (request.IsActive.HasValue)
                goal.IsActive = request.IsActive.Value;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return goal;
        });

        return new Response<GoalDto>
        {
            Success = true,
            Message = "Goal updated successfully.",
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
