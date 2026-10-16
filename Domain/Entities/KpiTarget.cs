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
}

public enum TargetGranularity
{
    Monthly = 1,
    Quarterly = 2,
    Annual = 3
}