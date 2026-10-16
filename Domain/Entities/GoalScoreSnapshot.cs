using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class GoalScoreSnapshot : CommonEntity, IScoreSnapshot
{
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid GoalId { get; set; }
    public Goal? Goal { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public SnapshotGranularity Granularity { get; set; }

    public decimal? Score { get; set; }

    public DateTime CalculatedAt { get; set; }
}
