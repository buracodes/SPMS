using Domain.Common;

namespace Domain.Entities;

public class KpiTarget : CommonEntity
{
    public Guid KpiId { get; set; }
    public Kpi? Kpi { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public decimal TargetValue { get; set; }

    public TargetGranularity Granularity { get; set; }

    /// <summary>
    /// True on a quarterly row whose value was computed from its three monthly targets.
    /// The monthly and quarterly rows satisfy the same reconciliation rule either way, so
    /// this is the only record of how they were produced; the target page restores its
    /// auto-calculate editor mode from it.
    /// </summary>
    public bool IsDerivedFromMonthly { get; set; }
}

public enum TargetGranularity
{
    Monthly = 1,
    Quarterly = 2,
    Annual = 3
}