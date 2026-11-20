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
        private readonly IOrganizationalScopeService organizationalScope;

        public GetDivisionPlanQueryHandler(
            IDivisionRepository _divisionRepository,
            IScorecardRepository _scorecardRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IGoalRepository _goalRepository,
            IDivisionPeriodCalculationService _divisionPeriodCalculationService,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IRagThresholdService _ragThresholdService,
            IOrganizationalScopeService _organizationalScope)
        {
            organizationalScope = _organizationalScope;
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

            var plan = await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(request.DivisionId, request.ScorecardId);
            if (plan == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.DivisionPlan), Guid.Empty);
            }

            // FR-901/FR-904: the plan's selected scorecard is the only goal source.
            var corporateGoals = await goalRepository.GetByScorecardAsync(scorecard.Id);

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
                plan, scorecard, corporateGoals, performance, scorecard, kpiTargets, fiscalPeriods, thresholds);
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
