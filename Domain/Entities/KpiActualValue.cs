using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class KpiActualValue : CommonEntity
{
    public Guid KpiId { get; set; }
    public Kpi? Kpi { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public decimal Value { get; set; }

    public KpiDataSource DataSource { get; set; }

    public DateTime AsOfDate { get; set; }

    public Guid? RecordedBy { get; set; }
    public User? Recorder { get; set; }

    public string? Note { get; set; }
}
