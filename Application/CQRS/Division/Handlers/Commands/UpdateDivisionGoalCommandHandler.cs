using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class UpdateDivisionGoalCommandHandler
        : IRequestHandler<UpdateDivisionGoalCommandRequest, Response<DivisionGoalDto>>
    {
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IDivisionContributionRepository contributionRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IUnitOfWork unitOfWork;

        public UpdateDivisionGoalCommandHandler(
            IDivisionGoalRepository _divisionGoalRepository,
            IDivisionContributionRepository _contributionRepository,
            IGoalRepository _goalRepository,
            IUnitOfWork _unitOfWork)
        {
            divisionGoalRepository = _divisionGoalRepository;
            contributionRepository = _contributionRepository;
            goalRepository = _goalRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionGoalDto>> Handle(
            UpdateDivisionGoalCommandRequest request, CancellationToken cancellationToken)
        {
            var goal = await divisionGoalRepository.GetByIdWithPlanAsync(request.DivisionGoalId)
                ?? throw new NotFoundException(nameof(DivisionGoal), request.DivisionGoalId);

            var plan = goal.DivisionPlan
                ?? throw new BadRequestException("The division goal is not attached to a plan.");

            if (plan.PlanStatus != DivisionPlanStatus.Draft)
                throw new BadRequestException("Division goals cannot be edited once the plan has been finalized or locked.");

            if (request.ParentGoalId.HasValue && request.ParentGoalId.Value != goal.ParentGoalId)
            {
                var contributed = await contributionRepository.ExistsInPlanAsync(plan.Id, request.ParentGoalId.Value);
                if (!contributed)
                    throw new BadRequestException("Division goals can only be attached to corporate goals the division contributes to.");

                goal.ParentGoalId = request.ParentGoalId.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var name = request.Name.Trim();
                if (await divisionGoalRepository.ExistsByNameInPlanAsync(plan.Id, name, goal.Id))
                    throw new BadRequestException($"A division goal named '{name}' already exists in this plan.");
                goal.Name = name;
            }

            if (request.Weight.HasValue && request.Weight.Value != goal.Weight)
            {
                var otherWeight = await divisionGoalRepository.GetWeightsSumAsync(plan.Id, goal.Id);
                if (otherWeight + request.Weight.Value > 100m)
                    throw new BadRequestException(
                        $"Updating this goal would bring the total division goal weight to {otherWeight + request.Weight.Value:F2}% which exceeds 100%.");

                goal.Weight = request.Weight.Value;
            }

            if (request.Description != null)
                goal.Description = request.Description;

            if (request.ContributionDescription != null)
                goal.ContributionDescription = request.ContributionDescription;

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var parent = await goalRepository.GetById(goal.ParentGoalId);
            var data = DivisionPlanDtoFactory.BuildGoal(goal);
            data.ParentGoalName = parent?.Name ?? string.Empty;

            return new Response<DivisionGoalDto>
            {
                Success = true,
                Message = "Division goal updated successfully.",
                Data = data
            };
        }
    }
}
