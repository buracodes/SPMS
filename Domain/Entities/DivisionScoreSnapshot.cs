using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class DivisionScoreSnapshot : CommonEntity
{
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }

    public Guid? DivisionGoalId { get; set; }
    public DivisionGoal? DivisionGoal { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public SnapshotGranularity Granularity { get; set; }

    public decimal? Score { get; set; }

    public DateTime CalculatedAt { get; set; }
}
