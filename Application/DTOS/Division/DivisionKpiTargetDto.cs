using Domain.Entities;

namespace Application.DTOS.Division
{
    /// <summary>
    /// A persisted KPI target returned with a division KPI so the entry form can reload the
    /// configured monthly/quarterly/annual values (FR-904).
    /// </summary>
    public class DivisionKpiTargetDto
    {
        public Guid FiscalPeriodId { get; set; }
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }
        public TargetGranularity Granularity { get; set; }
        public decimal TargetValue { get; set; }
    }
}
