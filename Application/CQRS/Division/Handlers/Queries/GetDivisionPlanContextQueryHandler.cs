using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Handlers.Queries
{
    public class GetDivisionPlanContextQueryHandler
        : IRequestHandler<GetDivisionPlanContextQueryRequest, Response<DivisionPlanDto>>
    {
        private readonly IScorecardRepository scorecardRepository;
        private readonly IDivisionRepository divisionRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IDivisionPeriodCalculationService divisionPeriodCalculationService;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IRagThresholdService ragThresholdService;
        private readonly IOrganizationalScopeService organizationalScope;

        public GetDivisionPlanContextQueryHandler(
            IScorecardRepository _scorecardRepository,
            IDivisionRepository _divisionRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IGoalRepository _goalRepository,
            IDivisionPeriodCalculationService _divisionPeriodCalculationService,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IRagThresholdService _ragThresholdService,
            IOrganizationalScopeService _organizationalScope)
        {
            organizationalScope = _organizationalScope;
            scorecardRepository = _scorecardRepository;
            divisionRepository = _divisionRepository;
            divisionPlanRepository = _divisionPlanRepository;
            goalRepository = _goalRepository;
            divisionPeriodCalculationService = _divisionPeriodCalculationService;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
            ragThresholdService = _ragThresholdService;
        }

        public async Task<Response<DivisionPlanDto>> Handle(
            GetDivisionPlanContextQueryRequest request, CancellationToken cancellationToken)
        {
            // A division account can only load its own plan. A corporate principal may load
            // any division, or omit the division to get the scorecard's first plan. Any other
            // principal is rejected: falling through to "first plan of the scorecard" would
            // expose an arbitrary division's plan to a user without cross-division visibility.
            if (request.DivisionId.HasValue)
            {
                await organizationalScope.EnsureDivisionAsync(request.DivisionId.Value, cancellationToken);
            }
            else if (organizationalScope.DivisionId.HasValue)
            {
                request.DivisionId = organizationalScope.DivisionId;
            }
            else if (!await organizationalScope.HasDivisionPortfolioAccessAsync(cancellationToken))
            {
                throw new ForbiddenException(
                    "A division must be specified and you do not have access to view division plans across divisions.");
            }

            var scorecard = await scorecardRepository.GetById(request.ScorecardId)
                ?? throw new NotFoundException(nameof(Scorecard), request.ScorecardId);

            var fiscalPeriods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);

            // FR-901/FR-904: the selected scorecard is the single source of plan goals.
            // There are no division-owned scorecards and no fallback to a different one.
            var corporateGoals = await goalRepository.GetByScorecardAsync(scorecard.Id);

            DivisionPlan? plan;
            if (request.DivisionId.HasValue)
            {
                plan = await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(
                    request.DivisionId.Value, request.ScorecardId);
            }
            else
            {
                // A corporate caller without a division claim must name the division when
                // several plans exist. Picking the first plan made the page non-deterministic.
                var plans = await divisionPlanRepository.GetByScorecardAsync(request.ScorecardId);
                if (plans.Count > 1)
                {
                    throw new BadRequestException("Select a division to load its plan.");
                }

                plan = plans.Count == 1
                    ? await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(
                        plans[0].DivisionId, request.ScorecardId)
                    : null;
            }

            if (plan == null)
            {
                Domain.Entities.Division? division = null;
                if (request.DivisionId.HasValue)
                {
                    division = await divisionRepository.GetById(request.DivisionId.Value);
                }

                return new Response<DivisionPlanDto>
                {
                    Success = true,
                    Message = "Division plan context retrieved successfully.",
                    Data = new DivisionPlanDto
                    {
                        Id = Guid.Empty,
                        DivisionId = division?.Id ?? request.DivisionId ?? Guid.Empty,
                        DivisionName = division?.Name ?? string.Empty,
                        ScorecardId = scorecard.Id,
                        ScorecardName = scorecard.Name,
                        FiscalYearId = scorecard.FiscalYearId,
                        PlanStatus = DivisionPlanStatus.Draft,
                        CorporateScorecardId = scorecard.Id,
                        CorporateScorecardName = scorecard.Name,
                        Contributions = new List<CorporateGoalDto>(),
                        AvailableGoals = corporateGoals
                            .OrderBy(g => g.Name)
                            .Select(ToCorporateGoal)
                            .ToList(),
                        Goals = new List<DivisionGoalDto>(),
                        FiscalPeriods = fiscalPeriods
                            .OrderBy(p => p.FiscalMonth)
                            .Select(ToFiscalPeriod)
                            .ToList()
                    }
                };
            }

            var performance = await divisionPeriodCalculationService.CalculateAsync(
                plan.Id,
                scorecard.Id,
                scorecard.FiscalYearId,
                request.PeriodType,
                request.Quarter,
                request.FromMonth,
                request.ToMonth,
                request.Year,
                cancellationToken);

            // FR-904: expose the persisted targets of this plan's KPIs so the entry form can
            // reload the monthly/quarterly/annual values when a KPI is edited.
            var planKpiIds = plan.Goals.SelectMany(g => g.Kpis).Select(k => k.Id).ToHashSet();
            var allTargets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var kpiTargets = allTargets
                .Where(t => planKpiIds.Contains(t.KpiId))
                .GroupBy(t => t.KpiId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<KpiTarget>)g.ToList());

            var thresholds = await ragThresholdService.GetEffectiveAsync(cancellationToken: cancellationToken);
            var data = DivisionPlanDtoFactory.Build(
                plan, scorecard, corporateGoals, performance, scorecard, kpiTargets, fiscalPeriods, thresholds);
            data.DivisionName = plan.Division?.Name ?? data.DivisionName;

            return new Response<DivisionPlanDto>
            {
                Success = true,
                Message = "Division plan context retrieved successfully.",
                Data = data
            };
        }

        private static CorporateGoalDto ToCorporateGoal(Goal goal) => new()
        {
            GoalId = goal.Id,
            Name = goal.Name,
            Description = goal.Description,
            Weight = goal.Weight
        };

        private static DivisionFiscalPeriodDto ToFiscalPeriod(FiscalPeriod period) => new()
        {
            Id = period.Id,
            FiscalMonth = period.FiscalMonth,
            Quarter = period.Quarter,
            IsQuarterEnd = period.IsQuarterEnd,
            IsYearEnd = period.IsYearEnd
        };
    }
}
