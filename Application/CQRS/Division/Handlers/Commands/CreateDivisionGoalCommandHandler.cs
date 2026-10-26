using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class CreateDivisionGoalCommandHandler : IRequestHandler<CreateDivisionGoalCommandRequest, Response<DivisionGoalDto>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IDivisionContributionRepository contributionRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IUnitOfWork unitOfWork;
        private readonly IOrganizationalScopeService organizationalScope;

        public CreateDivisionGoalCommandHandler(
            IDivisionRepository _divisionRepository,
            IScorecardRepository _scorecardRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IDivisionContributionRepository _contributionRepository,
            IDivisionGoalRepository _divisionGoalRepository,
            IGoalRepository _goalRepository,
            IUnitOfWork _unitOfWork,
            IOrganizationalScopeService _organizationalScope)
        {
            organizationalScope = _organizationalScope;
            divisionRepository = _divisionRepository;
            scorecardRepository = _scorecardRepository;
            divisionPlanRepository = _divisionPlanRepository;
            contributionRepository = _contributionRepository;
            divisionGoalRepository = _divisionGoalRepository;
            goalRepository = _goalRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionGoalDto>> Handle(CreateDivisionGoalCommandRequest request, CancellationToken cancellationToken)
        {
            await organizationalScope.EnsureDivisionAsync(request.DivisionId, cancellationToken);

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

            var plan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(division.Id, scorecard.Id);
            if (plan == null)
            {
                throw new BadRequestException("Select the corporate goals this division contributes to before creating division goals.");
            }

            if (plan.PlanStatus != DivisionPlanStatus.Draft)
            {
                throw new BadRequestException("Division goals cannot be added once the plan has been finalized or locked.");
            }

            var parentGoal = await goalRepository.GetById(request.ParentGoalId);
            if (parentGoal == null || parentGoal.ScorecardId != scorecard.Id)
            {
                throw new BadRequestException("The parent corporate goal does not belong to the selected scorecard.");
            }

            var contributed = await contributionRepository.ExistsInPlanAsync(plan.Id, parentGoal.Id);
            if (!contributed)
            {
                throw new BadRequestException("Division goals can only be created under corporate goals the division contributes to.");
            }

            if (await divisionGoalRepository.ExistsByNameInPlanAsync(plan.Id, request.Name.Trim()))
            {
                throw new BadRequestException($"A division goal named '{request.Name.Trim()}' already exists in this plan.");
            }

            var currentWeightSum = await divisionGoalRepository.GetWeightsSumAsync(plan.Id);
            if (currentWeightSum + request.Weight > 100m)
            {
                throw new BadRequestException(
                    $"Adding this goal would bring the total division goal weight to {currentWeightSum + request.Weight:F2}% which exceeds 100%. Adjust the weight or existing goals.");
            }

            var goal = new DivisionGoal
            {
                Id = Guid.NewGuid(),
                DivisionPlanId = plan.Id,
                ParentGoalId = parentGoal.Id,
                Name = request.Name.Trim(),
                Description = request.Description,
                Weight = request.Weight,
                ContributionDescription = request.ContributionDescription
            };

            await unitOfWork.AddRangeAsync(new[] { goal }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var data = DivisionPlanDtoFactory.BuildGoal(goal);
            data.ParentGoalName = parentGoal.Name;

            return new Response<DivisionGoalDto>
            {
                Success = true,
                Message = "Division goal created successfully.",
                Data = data
            };
        }
    }
}
