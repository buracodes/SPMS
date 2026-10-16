using Application.CQRS.Kpi.Requests.Queries;
using Application.DTOS.Kpi;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kpi.Handlers.Queries;

public class GetAllKpiQueryHandler : IRequestHandler<GetAllKpiQueryRequest, Response<List<GetAllKpiDto>>>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IKraRepository kraRepository;
    private readonly IGoalRepository goalRepository;

    public GetAllKpiQueryHandler(
        IKpiRepository _kpiRepository,
        IScorecardRepository _scorecardRepository,
        IKraRepository _kraRepository,
        IGoalRepository _goalRepository)
    {
        kpiRepository = _kpiRepository;
        scorecardRepository = _scorecardRepository;
        kraRepository = _kraRepository;
        goalRepository = _goalRepository;
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

        if (request.ScorecardId.HasValue)
            filtered = filtered.Where(k => k.ScorecardId == request.ScorecardId.Value);

        if (request.FiscalYearId.HasValue)
            filtered = filtered.Where(k =>
                scorecardById.TryGetValue(k.ScorecardId, out var scorecard) &&
                scorecard.FiscalYearId == request.FiscalYearId.Value);

        if (request.KraId.HasValue)
            filtered = filtered.Where(k => k.KraId == request.KraId.Value);

        // A KPI sits under a goal through its KRA, so the goal filter narrows the KRA set.
        if (request.GoalId.HasValue)
            filtered = filtered.Where(k =>
                k.KraId.HasValue &&
                kraById.TryGetValue(k.KraId.Value, out var kra) &&
                kra.GoalId == request.GoalId.Value);

        var dtos = filtered.Select(k =>
        {
            var scorecardName = scorecardById.TryGetValue(k.ScorecardId, out var scorecard) ? scorecard.Name : null;
            var kra = k.KraId.HasValue && kraById.TryGetValue(k.KraId.Value, out var found) ? found : null;
            var goalId = kra?.GoalId;
            var goalName = goalId.HasValue && goalNameById.TryGetValue(goalId.Value, out var name) ? name : null;

            return new GetAllKpiDto
            {
                Id = k.Id,
                ScorecardId = k.ScorecardId,
                ScorecardName = scorecardName,
                KraId = k.KraId,
                KraName = kra?.Name,
                GoalId = goalId,
                GoalName = goalName,
                DivisionGoalId = k.DivisionGoalId,
                OwnerId = k.OwnerId,
                OwnerName = k.Owner?.FullName,
                DivisionOwnerId = k.DivisionOwnerId,
                DivisionOwnerName = k.DivisionOwner?.Name,
                UnitOfMeasureId = k.UnitOfMeasureId,
                Name = k.Name,
                Description = k.Description,
                Unit = k.Unit,
                Weight = k.Weight,
                ToleranceBandPercent = k.ToleranceBandPercent,
                KpiType = k.KpiType,
                OptimizationType = k.OptimizationType,
                DataSource = k.DataSource,
                IsCumulative = k.IsCumulative,
                ParentKpiId = k.ParentKpiId,
                Baseline = k.Baseline,
                MinValue = k.MinValue,
                MaxValue = k.MaxValue,
                Formula = k.Formula,
                IsActive = k.IsActive,
                CreatedBy = k.CreatedBy,
                CreatedDate = k.CreatedDate,
                UpdatedBy = k.UpdatedBy,
                UpdatedDate = k.UpdatedDate,
                Status = k.Status
            };
        }).ToList();

        return new Response<List<GetAllKpiDto>>
        {
            Success = true,
            Message = "KPIs retrieved successfully.",
            Data = dtos
        };
    }
}
