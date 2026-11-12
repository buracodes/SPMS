using Application.DTOS.Kpi;
using Domain.Entities;

namespace Application.CQRS.Kpi;

/// <summary>
/// Projects a KPI entity onto the grid row contract. The KPI list and the Data Entry board
/// both map rows here, so the resolved display names cannot diverge between the two reads.
/// </summary>
public static class KpiRowMapper
{
    public static GetAllKpiDto Map(
        Domain.Entities.Kpi kpi,
        IReadOnlyDictionary<Guid, Domain.Entities.Scorecard> scorecardById,
        IReadOnlyDictionary<Guid, Domain.Entities.Kra> kraById,
        IReadOnlyDictionary<Guid, string> goalNameById)
    {
        var scorecardName = scorecardById.TryGetValue(kpi.ScorecardId, out var scorecard) ? scorecard.Name : null;
        var kra = kpi.KraId.HasValue && kraById.TryGetValue(kpi.KraId.Value, out var found) ? found : null;
        var goalId = kra?.GoalId ?? kpi.DivisionGoal?.ParentGoalId;
        var goalName = goalId.HasValue && goalNameById.TryGetValue(goalId.Value, out var name) ? name : null;

        return new GetAllKpiDto
        {
            Id = kpi.Id,
            ScorecardId = kpi.ScorecardId,
            ScorecardName = scorecardName,
            KraId = kpi.KraId,
            KraName = kra?.Name,
            GoalId = goalId,
            GoalName = goalName,
            DivisionGoalId = kpi.DivisionGoalId,
            OwnerId = kpi.OwnerId,
            OwnerName = kpi.Owner?.FullName,
            DivisionOwnerId = kpi.DivisionOwnerId,
            DivisionOwnerName = kpi.DivisionOwner?.Name,
            UnitOfMeasureId = kpi.UnitOfMeasureId,
            Name = kpi.Name,
            Description = kpi.Description,
            Unit = kpi.Unit,
            Weight = kpi.Weight,
            ToleranceBandPercent = kpi.ToleranceBandPercent,
            KpiType = kpi.KpiType,
            OptimizationType = kpi.OptimizationType,
            DataSource = kpi.DataSource,
            IsCumulative = kpi.IsCumulative,
            ParentKpiId = kpi.ParentKpiId,
            Baseline = kpi.Baseline,
            MinValue = kpi.MinValue,
            MaxValue = kpi.MaxValue,
            Formula = kpi.Formula,
            IsActive = kpi.IsActive,
            CreatedBy = kpi.CreatedBy,
            CreatedDate = kpi.CreatedDate,
            UpdatedBy = kpi.UpdatedBy,
            UpdatedDate = kpi.UpdatedDate,
            Status = kpi.Status
        };
    }
}
