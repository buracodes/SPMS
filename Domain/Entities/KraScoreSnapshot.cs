using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class KraScoreSnapshot : CommonEntity, IScoreSnapshot
{
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid KraId { get; set; }
    public Kra? Kra { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public SnapshotGranularity Granularity { get; set; }

    public decimal? Score { get; set; }

    public DateTime CalculatedAt { get; set; }
}
