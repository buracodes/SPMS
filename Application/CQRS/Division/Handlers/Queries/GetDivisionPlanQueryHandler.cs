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
    public class GetDivisionPlanQueryHandler : IRequestHandler<GetDivisionPlanQueryRequest, Response<DivisionPlanDto>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IDivisionPeriodCalculationService divisionPeriodCalculationService;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IRagThresholdService ragThresholdService;

        public GetDivisionPlanQueryHandler(
            IDivisionRepository _divisionRepository,
            IScorecardRepository _scorecardRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IGoalRepository _goalRepository,
            IDivisionPeriodCalculationService _divisionPeriodCalculationService,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IRagThresholdService _ragThresholdService)
        {
            divisionRepository = _divisionRepository;
            scorecardRepository = _scorecardRepository;
            divisionPlanRepository = _divisionPlanRepository;
            goalRepository = _goalRepository;
            divisionPeriodCalculationService = _divisionPeriodCalculationService;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
            ragThresholdService = _ragThresholdService;
        }

        public async Task<Response<DivisionPlanDto>> Handle(GetDivisionPlanQueryRequest request, CancellationToken cancellationToken)
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

            var plan = await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(request.DivisionId, request.ScorecardId);
            if (plan == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.DivisionPlan), Guid.Empty);
            }

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

            var fiscalPeriods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);
            var planKpiIds = plan.Goals.SelectMany(g => g.Kpis).Select(k => k.Id).ToHashSet();
            var allTargets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var kpiTargets = allTargets
                .Where(t => planKpiIds.Contains(t.KpiId))
                .GroupBy(t => t.KpiId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<KpiTarget>)g.ToList());

            var thresholds = await ragThresholdService.GetEffectiveAsync(cancellationToken: cancellationToken);
            var data = DivisionPlanDtoFactory.Build(
                plan, scorecard, corporateGoals, performance, corporateScorecard, kpiTargets, fiscalPeriods, thresholds);
            data.DivisionName = division.Name;

            return new Response<DivisionPlanDto>
            {
                Success = true,
                Message = "Division plan retrieved successfully.",
                Data = data
            };
        }
    }
}
