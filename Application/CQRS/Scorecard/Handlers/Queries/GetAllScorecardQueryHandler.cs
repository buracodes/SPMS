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

    public GetAllScorecardQueryHandler(
        IScorecardRepository _scorecardRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IGoalRepository _goalRepository,
        IKraRepository _kraRepository,
        IKpiRepository _kpiRepository)
    {
        scorecardRepository = _scorecardRepository;
        fiscalYearRepository = _fiscalYearRepository;
        goalRepository = _goalRepository;
        kraRepository = _kraRepository;
        kpiRepository = _kpiRepository;
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

        // Corporate counts only: division KPIs share the corporate scorecard id but belong to
        // division planning, whose goals live in a separate table.
        var kpiCountByScorecard = (await kpiRepository.GetAll())
            .Where(k => !k.DivisionGoalId.HasValue)
            .GroupBy(k => k.ScorecardId)
            .ToDictionary(k => k.Key, k => k.Count());

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
            GoalCount = goalCountByScorecard.GetValueOrDefault(s.Id),
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
