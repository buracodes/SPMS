using Domain.Enums;

namespace Application.DTOS.KpiActual
{
    public class EnterActualValueDto
    {
        public Guid KpiId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public decimal Value { get; set; }
        public KpiDataSource DataSource { get; set; } = KpiDataSource.Manual;
        public string? Note { get; set; }

        /// <summary>
        /// ECX-approved business rule (FR-703): a closed fiscal year / scorecard is normally
        /// read-only, but an Admin/Power User holding the <c>actuals:override</c> permission
        /// may still correct an actual value by supplying an audited reason here. The reason
        /// is persisted on the append-only audit record. This exception applies to actual
        /// values only — it never reopens targets or scorecard structure.
        /// </summary>
        public string? OverrideReason { get; set; }
    }
}
