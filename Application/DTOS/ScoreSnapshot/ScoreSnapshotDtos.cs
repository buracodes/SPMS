using Domain.Enums;

namespace Application.DTOS.ScoreSnapshot;

public class ScoreSnapshotDto
{
    public Guid EntityId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public int FiscalMonth { get; set; }
    public int Quarter { get; set; }
    public SnapshotGranularity Granularity { get; set; }
    public bool IsPeriodAggregate { get; set; }
    public decimal? Score { get; set; }
    public decimal? ActualValue { get; set; }
    public decimal? TargetValue { get; set; }
    public DateTime CalculatedAt { get; set; }
}

public class GetScoreSnapshotsDto
{
    public Guid ScorecardId { get; set; }
    public Guid FiscalYearId { get; set; }
    public IReadOnlyList<ScoreSnapshotDto> KpiSnapshots { get; set; } = new List<ScoreSnapshotDto>();
    public IReadOnlyList<ScoreSnapshotDto> KraSnapshots { get; set; } = new List<ScoreSnapshotDto>();
    public IReadOnlyList<ScoreSnapshotDto> GoalSnapshots { get; set; } = new List<ScoreSnapshotDto>();
    public IReadOnlyList<ScoreSnapshotDto> ScorecardSnapshots { get; set; } = new List<ScoreSnapshotDto>();
}
