using Domain.Entities;

namespace Application.DTOS.Division
{
    /// <summary>
    /// One target value submitted with a division KPI. Multiple entries make up the full
    /// monthly/quarterly/annual target configuration described by FR-904.
    /// </summary>
    public class DivisionKpiTargetInputDto
    {
        public Guid FiscalPeriodId { get; set; }
        public decimal TargetValue { get; set; }

        /// <summary>Defaults to Monthly so period-only callers keep the previous behaviour.</summary>
        public TargetGranularity Granularity { get; set; } = TargetGranularity.Monthly;
    }
}
