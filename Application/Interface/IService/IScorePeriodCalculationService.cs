using Domain.Enums;

namespace Application.Interface.IService;

public sealed record ScorePeriodScope(int FromMonth, int ToMonth, SnapshotGranularity Granularity, int Quarter);

public sealed class ScorePeriodResult
{
    public ScorePeriodScope Scope { get; init; } = new(1, 12, SnapshotGranularity.Ytd, 1);
    public IReadOnlyDictionary<Guid, KpiScorePeriod> Kpis { get; init; } = new Dictionary<Guid, KpiScorePeriod>();
    public IReadOnlyDictionary<Guid, EntityScorePeriod> Kras { get; init; } = new Dictionary<Guid, EntityScorePeriod>();
    public IReadOnlyDictionary<Guid, EntityScorePeriod> Goals { get; init; } = new Dictionary<Guid, EntityScorePeriod>();
    public IReadOnlyDictionary<Guid, EntityScorePeriod> Scorecards { get; init; } = new Dictionary<Guid, EntityScorePeriod>();
    public IReadOnlyDictionary<int, Guid> FiscalPeriodByMonth { get; init; } = new Dictionary<int, Guid>();
}

public sealed class KpiScorePeriod
{
    public decimal? PeriodScore { get; set; }
    public decimal? PeriodTarget { get; set; }
    public decimal? PeriodActual { get; set; }
    public IReadOnlyDictionary<int, decimal?> MonthlyScores { get; set; } = new Dictionary<int, decimal?>();
    public IReadOnlyDictionary<int, decimal?> MonthlyTargets { get; set; } = new Dictionary<int, decimal?>();
    public IReadOnlyDictionary<int, decimal?> MonthlyActuals { get; set; } = new Dictionary<int, decimal?>();
}

public sealed class EntityScorePeriod
{
    public decimal? PeriodScore { get; set; }
    public IReadOnlyDictionary<int, decimal?> MonthlyScores { get; set; } = new Dictionary<int, decimal?>();
}

public interface IScorePeriodCalculationService
{
    Task<ScorePeriodResult> CalculateAsync(
        Guid scorecardId,
        Guid fiscalYearId,
        PeriodType? periodType,
        int? quarter,
        int? fromMonth,
        int? toMonth,
        int? year,
        CancellationToken cancellationToken = default);
}