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
            else if (!await organizationalScope.HasCorporateAccessAsync(cancellationToken))
            {
                throw new ForbiddenException(
                    "A division must be specified and you do not have access to view division plans across divisions.");
            }

            var scorecard = await scorecardRepository.GetById(request.ScorecardId)
                ?? throw new NotFoundException(nameof(Scorecard), request.ScorecardId);

            var fiscalPeriods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);

            var corporateScorecard = scorecardRepository.GetQueryable()
                .Where(s => s.FiscalYearId == scorecard.FiscalYearId
                            && s.Scope == ScorecardScope.Corporate
                            && s.Id != scorecard.Id)
                .OrderBy(s => s.Name)
                .FirstOrDefault();

            // When the plan scorecard is itself the corporate scorecard (legacy layout),
            // fall back to its own goals.
            corporateScorecard ??= scorecard;
            var corporateGoals = await goalRepository.GetByScorecardAsync(corporateScorecard.Id);

            var plan = request.DivisionId.HasValue
                ? await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(
                    request.DivisionId.Value, request.ScorecardId)
                : await divisionPlanRepository.GetFirstByScorecardWithDetailsAsync(request.ScorecardId);

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
                        CorporateScorecardId = corporateScorecard?.Id,
                        CorporateScorecardName = corporateScorecard?.Name,
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
                plan, scorecard, corporateGoals, performance, corporateScorecard, kpiTargets, fiscalPeriods, thresholds);
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
