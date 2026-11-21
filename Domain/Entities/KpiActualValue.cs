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

    /// <summary>
    /// Exactly one actual per KPI + fiscal period is current. Corrections are append-only:
    /// the previous row is marked non-current instead of being deleted, so every revision
    /// remains available to an authorized history query.
    /// </summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>
    /// The previous revision superseded by this row. The self-FK is configured with RESTRICT
    /// so a revision that is referenced by a later correction can never be physically deleted.
    /// </summary>
    public Guid? SupersedesActualId { get; set; }
}
