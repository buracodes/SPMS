using Application.CQRS.Scorecard.Requests.Queries;
using Application.DTOS.Scorecard;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Scorecard.Handlers.Queries;

public class GetAllScorecardQueryHandler : IRequestHandler<GetAllScorecardQueryRequest, Response<List<GetAllScorecardDto>>>
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKraRepository kraRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;

    public GetAllScorecardQueryHandler(
        IScorecardRepository _scorecardRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IGoalRepository _goalRepository,
        IKraRepository _kraRepository,
        IKpiRepository _kpiRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository)
    {
        scorecardRepository = _scorecardRepository;
        fiscalYearRepository = _fiscalYearRepository;
        goalRepository = _goalRepository;
        kraRepository = _kraRepository;
        kpiRepository = _kpiRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
    }

    public async Task<Response<List<GetAllScorecardDto>>> Handle(GetAllScorecardQueryRequest request, CancellationToken cancellationToken)
    {
        var scorecards = await scorecardRepository.GetAll();
        var fiscalYears = await fiscalYearRepository.GetAll();
        var fyDict = fiscalYears.ToDictionary(f => f.Id, f => f.Label);

        var goalCountByScorecard = (await goalRepository.GetAll())
            .GroupBy(g => g.ScorecardId)
            .ToDictionary(g => g.Key, g => g.Count());

        var kraCountByScorecard = (await kraRepository.GetAll())
            .GroupBy(k => k.ScorecardId)
            .ToDictionary(k => k.Key, k => k.Count());

        var kpiCountByScorecard = (await kpiRepository.GetAll())
            .GroupBy(k => k.ScorecardId)
            .ToDictionary(k => k.Key, k => k.Count());

        var divisionPlans = await divisionPlanRepository.GetAll();
        var divisionGoalsByPlan = (await divisionGoalRepository.GetAll())
            .GroupBy(g => g.DivisionPlanId)
            .ToDictionary(g => g.Key, g => g.Count());

        var divisionGoalCountByScorecard = divisionPlans
            .GroupBy(p => p.ScorecardId)
            .ToDictionary(p => p.Key, p => p.Sum(plan => divisionGoalsByPlan.GetValueOrDefault(plan.Id)));

        var filtered = request.FiscalYearId.HasValue
            ? scorecards.Where(s => s.FiscalYearId == request.FiscalYearId.Value)
            : scorecards;

        var dtos = filtered.Select(s => new GetAllScorecardDto
        {
            Id = s.Id,
            FiscalYearId = s.FiscalYearId,
            FiscalYearName = fyDict.TryGetValue(s.FiscalYearId, out var fyName) ? fyName : null,
            Name = s.Name,
            Description = s.Description,
            Scope = s.Scope,
            ScorecardStatus = s.ScorecardStatus,
            GoalCount = goalCountByScorecard.GetValueOrDefault(s.Id) + divisionGoalCountByScorecard.GetValueOrDefault(s.Id),
            KraCount = kraCountByScorecard.GetValueOrDefault(s.Id),
            KpiCount = kpiCountByScorecard.GetValueOrDefault(s.Id),
            CreatedDate = s.CreatedDate
        }).ToList();

        return new Response<List<GetAllScorecardDto>>
        {
            Success = true,
            Message = "Scorecards retrieved successfully.",
            Data = dtos
        };
    }
}
