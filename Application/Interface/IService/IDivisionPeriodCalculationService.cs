using Domain.Enums;

namespace Application.Interface.IService;

/// <summary>
/// Period-scoped score result for a single division plan. Division goals contain
/// KPIs directly (no KRA layer), so scores roll up KPI → division goal → overall.
/// </summary>
public sealed class DivisionPeriodResult
{
    public ScorePeriodScope Scope { get; init; } = new(1, 12, SnapshotGranularity.Ytd, 1);

    /// <summary>Period score per division KPI id.</summary>
    public IReadOnlyDictionary<Guid, KpiScorePeriod> Kpis { get; init; } =
        new Dictionary<Guid, KpiScorePeriod>();

    /// <summary>Period score per division goal id.</summary>
    public IReadOnlyDictionary<Guid, EntityScorePeriod> DivisionGoals { get; init; } =
        new Dictionary<Guid, EntityScorePeriod>();

    /// <summary>Overall division scorecard score for the active period.</summary>
    public decimal? OverallScore { get; init; }

    public IReadOnlyDictionary<int, Guid> FiscalPeriodByMonth { get; init; } =
        new Dictionary<int, Guid>();
}

public interface IDivisionPeriodCalculationService
{
    Task<DivisionPeriodResult> CalculateAsync(
        Guid divisionPlanId,
        Guid scorecardId,
        Guid fiscalYearId,
        PeriodType? periodType,
        int? quarter,
        int? fromMonth,
        int? toMonth,
        int? year,
        CancellationToken cancellationToken = default);
}
