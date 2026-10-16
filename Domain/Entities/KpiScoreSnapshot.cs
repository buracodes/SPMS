using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class KpiScoreSnapshot : CommonEntity, IScoreSnapshot
{
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid KpiId { get; set; }
    public Kpi? Kpi { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public SnapshotGranularity Granularity { get; set; }

    public decimal? Score { get; set; }

    public decimal? ActualValue { get; set; }

    public decimal? TargetValue { get; set; }

    public DateTime CalculatedAt { get; set; }
}
