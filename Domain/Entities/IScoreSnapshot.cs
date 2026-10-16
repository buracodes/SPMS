using Domain.Enums;

namespace Domain.Entities;

public interface IScoreSnapshot
{
    Guid ScorecardId { get; }
    Guid FiscalPeriodId { get; }
    FiscalPeriod? FiscalPeriod { get; }
    SnapshotGranularity Granularity { get; }
    decimal? Score { get; }
    DateTime CalculatedAt { get; }
}
