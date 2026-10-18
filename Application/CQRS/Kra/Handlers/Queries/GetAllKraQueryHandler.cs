using Application.CQRS.Kra.Requests.Queries;
using Application.DTOS.Kra;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kra.Handlers.Queries;

public class GetAllKraQueryHandler : IRequestHandler<GetAllKraQueryRequest, Response<List<GetAllKraDto>>>
{
    private readonly IKraRepository kraRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKpiRepository kpiRepository;

    public GetAllKraQueryHandler(
        IKraRepository _kraRepository,
        IScorecardRepository _scorecardRepository,
        IGoalRepository _goalRepository,
        IKpiRepository _kpiRepository)
    {
        kraRepository = _kraRepository;
        scorecardRepository = _scorecardRepository;
        goalRepository = _goalRepository;
        kpiRepository = _kpiRepository;
    }

    public async Task<Response<List<GetAllKraDto>>> Handle(GetAllKraQueryRequest request, CancellationToken cancellationToken)
    {
        var kras = await kraRepository.GetAll();
        var scorecards = await scorecardRepository.GetAll();
        var goals = await goalRepository.GetAll();

        var scorecardById = scorecards.ToDictionary(s => s.Id);
        var goalNameById = goals.ToDictionary(g => g.Id, g => g.Name);

        // Kras are loaded without their KPIs, so the counts come from a single grouped read.
        var kpiCountByKra = (await kpiRepository.GetAll())
            .Where(k => k.KraId.HasValue)
            .GroupBy(k => k.KraId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var filtered = kras.AsEnumerable();

        if (request.ScorecardId.HasValue)
            filtered = filtered.Where(k => k.ScorecardId == request.ScorecardId.Value);

        if (request.FiscalYearId.HasValue)
            filtered = filtered.Where(k =>
                scorecardById.TryGetValue(k.ScorecardId, out var scorecard) &&
                scorecard.FiscalYearId == request.FiscalYearId.Value);

        if (request.GoalId.HasValue)
            filtered = filtered.Where(k => k.GoalId == request.GoalId.Value);

        var dtos = filtered.Select(k => new GetAllKraDto
        {
            Id = k.Id,
            GoalId = k.GoalId,
            GoalName = goalNameById.TryGetValue(k.GoalId, out var goalName) ? goalName : null,
            ScorecardId = k.ScorecardId,
            ScorecardName = scorecardById.TryGetValue(k.ScorecardId, out var scorecard) ? scorecard.Name : null,
            Name = k.Name,
            Description = k.Description,
            Weight = k.Weight,
            IsActive = k.IsActive,
            KpiCount = kpiCountByKra.GetValueOrDefault(k.Id),
            Icon = k.Icon,
            StrategicThemeId = k.StrategicThemeId,
            UnitMeasure = k.UnitMeasure,
            CreatedBy = k.CreatedBy,
            CreatedDate = k.CreatedDate,
            UpdatedBy = k.UpdatedBy,
            UpdatedDate = k.UpdatedDate,
            Status = k.Status
        }).ToList();

        return new Response<List<GetAllKraDto>>
        {
            Success = true,
            Message = "KRAs retrieved successfully.",
            Data = dtos
        };
    }
}
