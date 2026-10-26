using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class FinalizeDivisionPlanCommandHandler
        : IRequestHandler<FinalizeDivisionPlanCommandRequest, Response<DivisionPlanSummaryDto>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly ICurrentUserService currentUserService;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IOrganizationalScopeService organizationalScope;

        public FinalizeDivisionPlanCommandHandler(
            IDivisionRepository _divisionRepository,
            IScorecardRepository _scorecardRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            ICurrentUserService _currentUserService,
            IKpiTargetRepository _kpiTargetRepository,
            IOrganizationalScopeService _organizationalScope)
        {
            organizationalScope = _organizationalScope;
            divisionRepository = _divisionRepository;
            scorecardRepository = _scorecardRepository;
            divisionPlanRepository = _divisionPlanRepository;
            currentUserService = _currentUserService;
            kpiTargetRepository = _kpiTargetRepository;
        }

        public async Task<Response<DivisionPlanSummaryDto>> Handle(
            FinalizeDivisionPlanCommandRequest request, CancellationToken cancellationToken)
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

            var plan = await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(division.Id, scorecard.Id);
            if (plan == null)
            {
                throw new BadRequestException("No division plan exists for this division and scorecard. Link corporate goals before finalizing.");
            }

            if (plan.PlanStatus != DivisionPlanStatus.Draft)
            {
                throw new BadRequestException(
                    plan.PlanStatus == DivisionPlanStatus.Finalized
                        ? "This division plan has already been finalized."
                        : "This division plan is locked and cannot be finalized.");
            }

            ValidatePlanIntegrity(plan);

            await ValidateKpiTargetsAsync(plan, scorecard);

            plan.PlanStatus = DivisionPlanStatus.Finalized;
            plan.FinalizedAtUtc = DateTime.UtcNow;
            plan.FinalizedByUserId = currentUserService.UserId;

            await divisionPlanRepository.Update(plan);

            var data = new DivisionPlanSummaryDto
            {
                Id = plan.Id,
                DivisionId = plan.DivisionId,
                ScorecardId = plan.ScorecardId,
                ScorecardName = scorecard.Name,
                FiscalYearId = scorecard.FiscalYearId,
                PlanStatus = plan.PlanStatus,
                GoalCount = plan.Goals.Count,
                KpiCount = plan.Goals.Sum(g => g.Kpis.Count)
            };

            return new Response<DivisionPlanSummaryDto>
            {
                Success = true,
                Message = "Division plan finalized successfully.",
                Data = data
            };
        }

        // FR-903 step 4 / FR-904: finalization is the plan-completion checkpoint, so the
        // weights must be *complete* (exactly 100%) rather than merely not exceeding 100%.
        // Add/edit operations only enforce "<= 100%" (FR-905); the exact-total rule lives
        // here so a half-weighted draft can still be saved and adjusted incrementally.
        // A small epsilon absorbs decimal rounding in stored weights.
        private const decimal RequiredTotalWeight = 100m;
        private const decimal WeightEpsilon = 0.01m;

        private static void ValidatePlanIntegrity(DivisionPlan plan)
        {
            if (plan.Goals.Count == 0)
            {
                throw new BadRequestException("Add at least one division goal before finalizing this plan.");
            }

            var totalGoalWeight = plan.Goals.Sum(g => g.Weight);
            if (Math.Abs(totalGoalWeight - RequiredTotalWeight) > WeightEpsilon)
            {
                throw new BadRequestException(
                    $"Division goal weights must total 100% before finalizing (currently {totalGoalWeight:F2}%).");
            }

            var totalKpis = plan.Goals.Sum(g => g.Kpis.Count);
            if (totalKpis == 0)
            {
                throw new BadRequestException("Add at least one division KPI before finalizing this plan.");
            }

            var incompleteKpiWeightGoal = plan.Goals.FirstOrDefault(
                g => g.Kpis.Count > 0 && Math.Abs(g.Kpis.Sum(k => k.Weight) - RequiredTotalWeight) > WeightEpsilon);
            if (incompleteKpiWeightGoal != null)
            {
                var sum = incompleteKpiWeightGoal.Kpis.Sum(k => k.Weight);
                throw new BadRequestException(
                    $"KPI weights under the division goal '{incompleteKpiWeightGoal.Name}' must total 100% before finalizing (currently {sum:F2}%).");
            }
        }

        // FR-904: targets are optional while a plan is a draft (a bare KPI can be added and
        // adjusted incrementally), but every KPI must carry targets before the plan can be
        // finalised — DTOs alone are not enough to score a period. The target reconciliation
        // rules were already enforced by DivisionKpiTargetBuilder on every create/update, so a
        // presence check here is sufficient.
        private async Task ValidateKpiTargetsAsync(DivisionPlan plan, Domain.Entities.Scorecard scorecard)
        {
            var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var kpiIdsWithTargets = targets.Select(t => t.KpiId).ToHashSet();

            var untargetedKpiNames = plan.Goals
                .SelectMany(g => g.Kpis)
                .Where(k => !kpiIdsWithTargets.Contains(k.Id))
                .Select(k => k.Name)
                .ToList();

            if (untargetedKpiNames.Count > 0)
            {
                throw new BadRequestException(
                    $"Every division KPI must have targets before finalizing. Missing targets for: {string.Join(", ", untargetedKpiNames)}. Open each KPI and enter its annual (and quarterly/monthly, where applicable) targets.");
            }
        }
    }
}