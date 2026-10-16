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
    public class SetDivisionContributionsCommandHandler : IRequestHandler<SetDivisionContributionsCommandRequest, Response<DivisionPlanDto>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IDivisionContributionRepository contributionRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IUnitOfWork unitOfWork;

        public SetDivisionContributionsCommandHandler(
            IDivisionRepository _divisionRepository,
            IScorecardRepository _scorecardRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IDivisionContributionRepository _contributionRepository,
            IDivisionGoalRepository _divisionGoalRepository,
            IGoalRepository _goalRepository,
            IUnitOfWork _unitOfWork)
        {
            divisionRepository = _divisionRepository;
            scorecardRepository = _scorecardRepository;
            divisionPlanRepository = _divisionPlanRepository;
            contributionRepository = _contributionRepository;
            divisionGoalRepository = _divisionGoalRepository;
            goalRepository = _goalRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionPlanDto>> Handle(SetDivisionContributionsCommandRequest request, CancellationToken cancellationToken)
        {
            var division = await divisionRepository.GetById(request.DivisionId);
            if (division == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionId);
            }

            var scorecard = await scorecardRepository.GetById(request.ScorecardId);
            if (scorecard == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.Scorecard), request.ScorecardId);
            }

            var goalIds = request.GoalIds.Distinct().ToList();
            var validGoalIds = await goalRepository.GetGoalIdsByScorecardAsync(scorecard.Id);

            var invalidGoalIds = goalIds.Where(id => !validGoalIds.Contains(id)).ToList();
            if (invalidGoalIds.Count > 0)
            {
                throw new BadRequestException("Some selected goals do not belong to the selected scorecard.");
            }

            var plan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(division.Id, scorecard.Id);
            if (plan == null)
            {
                plan = new Domain.Entities.DivisionPlan
                {
                    Id = Guid.NewGuid(),
                    DivisionId = division.Id,
                    ScorecardId = scorecard.Id,
                    PlanStatus = DivisionPlanStatus.Draft
                };
                await unitOfWork.AddRangeAsync(new[] { plan }, cancellationToken);
            }
            else if (plan.PlanStatus != DivisionPlanStatus.Draft)
            {
                throw new BadRequestException("Contributions cannot be modified once the plan has been finalized or locked.");
            }

            var existing = await contributionRepository.GetByPlanAsync(plan.Id);
            var existingGoalIds = existing.Select(c => c.GoalId).ToHashSet();
            var removedGoalIds = existingGoalIds.Where(id => !goalIds.Contains(id)).ToList();

            var cascadeGoals = new List<Domain.Entities.DivisionGoal>();
            if (removedGoalIds.Count > 0)
            {
                var hasDivisionGoals = await divisionGoalRepository.ExistsUnderParentGoalsAsync(plan.Id, removedGoalIds);
                // FR-902: keep-or-delete. When the user confirms deletion (CascadeRemoveDependents),
                // the dependent goals and KPIs are soft-deleted. Without it the contribution is
                // removed but the division goals and KPIs are kept in the plan.
                if (hasDivisionGoals && request.CascadeRemoveDependents)
                {
                    var planGoals = await divisionGoalRepository.GetByPlanWithKpisAsync(plan.Id);
                    cascadeGoals = planGoals
                        .Where(g => removedGoalIds.Contains(g.ParentGoalId))
                        .ToList();

                    var now = DateTime.UtcNow;
                    foreach (var goal in cascadeGoals)
                    {
                        foreach (var kpi in goal.Kpis)
                        {
                            kpi.IsDeleted = true;
                            kpi.Status = EntityStatus.Inactive;
                            kpi.UpdatedDate = now;
                        }

                        goal.IsDeleted = true;
                        goal.Status = EntityStatus.Inactive;
                        goal.UpdatedDate = now;
                    }
                }
            }

            var toRemove = existing.Where(c => removedGoalIds.Contains(c.GoalId)).ToList();
            if (toRemove.Count > 0)
            {
                await unitOfWork.RemoveRangeAsync(toRemove, cancellationToken);
            }

            var added = goalIds
                .Where(id => !existingGoalIds.Contains(id))
                .Select(id => new DivisionContribution
                {
                    Id = Guid.NewGuid(),
                    DivisionPlanId = plan.Id,
                    ScorecardId = scorecard.Id,
                    GoalId = id
                })
                .ToList();

            if (added.Count > 0)
            {
                await unitOfWork.AddRangeAsync(added, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return await BuildPlanResponseAsync(division.Id, scorecard.Id, cancellationToken);
        }

        private async Task<Response<DivisionPlanDto>> BuildPlanResponseAsync(Guid divisionId, Guid scorecardId, CancellationToken cancellationToken)
        {
            var scorecard = await scorecardRepository.GetById(scorecardId);
            var plan = await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(divisionId, scorecardId);
            var corporateGoals = await goalRepository.GetByScorecardAsync(scorecardId);

            var data = DivisionPlanDtoFactory.Build(plan!, scorecard!, corporateGoals);
            return new Response<DivisionPlanDto>
            {
                Success = true,
                Message = "Division contributions updated successfully.",
                Data = data
            };
        }
    }
}
