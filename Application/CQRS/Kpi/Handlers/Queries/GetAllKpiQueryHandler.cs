using Application.CQRS.Kpi.Requests.Queries;
using Application.DTOS.Kpi;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kpi.Handlers.Queries;

public class GetAllKpiQueryHandler : IRequestHandler<GetAllKpiQueryRequest, Response<List<GetAllKpiDto>>>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IKraRepository kraRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IOrganizationalScopeService organizationalScope;

    public GetAllKpiQueryHandler(
        IKpiRepository _kpiRepository,
        IScorecardRepository _scorecardRepository,
        IKraRepository _kraRepository,
        IGoalRepository _goalRepository,
        IOrganizationalScopeService _organizationalScope)
    {
        kpiRepository = _kpiRepository;
        scorecardRepository = _scorecardRepository;
        kraRepository = _kraRepository;
        goalRepository = _goalRepository;
        organizationalScope = _organizationalScope;
    }

    public async Task<Response<List<GetAllKpiDto>>> Handle(GetAllKpiQueryRequest request, CancellationToken cancellationToken)
    {
        var kpis = await kpiRepository.GetAllKpisWithDetailsAsync();
        var scorecards = await scorecardRepository.GetAll();
        var kras = await kraRepository.GetAll();
        var goals = await goalRepository.GetAll();

        var scorecardById = scorecards.ToDictionary(s => s.Id);
        var kraById = kras.ToDictionary(k => k.Id);
        var goalNameById = goals.ToDictionary(g => g.Id, g => g.Name);

        var filtered = kpis.AsEnumerable();

        // Corporate visibility comes from the live authorization state. A principal without
        // it only sees KPIs owned by its divisions; with no division assignment there is no
        // corporate fallback, so the catalog stays empty instead of exposing corporate data.
        if (!await organizationalScope.HasCorporateAccessAsync(cancellationToken))
        {
            var divisionIds = organizationalScope.DivisionIds;
            filtered = filtered.Where(k =>
                divisionIds.Count > 0 &&
                ((k.OwnerId.HasValue && k.Owner?.DivisionId is Guid ownerDivisionId && divisionIds.Contains(ownerDivisionId)) ||
                 (k.DivisionOwnerId.HasValue && divisionIds.Contains(k.DivisionOwnerId.Value))));
        }

        if (request.ScorecardId.HasValue)
            filtered = filtered.Where(k => k.ScorecardId == request.ScorecardId.Value);

        if (request.FiscalYearId.HasValue)
            filtered = filtered.Where(k =>
                scorecardById.TryGetValue(k.ScorecardId, out var scorecard) &&
                scorecard.FiscalYearId == request.FiscalYearId.Value);

        if (request.KraId.HasValue)
            filtered = filtered.Where(k => k.KraId == request.KraId.Value);

        // A corporate KPI sits under a goal through its KRA; a division KPI links to the
        // corporate goal directly through its division goal, so both paths are matched.
        if (request.GoalId.HasValue)
            filtered = filtered.Where(k =>
                (k.KraId.HasValue &&
                 kraById.TryGetValue(k.KraId.Value, out var kra) &&
                 kra.GoalId == request.GoalId.Value) ||
                (k.DivisionGoal != null && k.DivisionGoal.ParentGoalId == request.GoalId.Value));

        var dtos = filtered
            .Select(k => KpiRowMapper.Map(k, scorecardById, kraById, goalNameById))
            .ToList();

        return new Response<List<GetAllKpiDto>>
        {
            Success = true,
            Message = "KPIs retrieved successfully.",
            Data = dtos
        };
    }
}
