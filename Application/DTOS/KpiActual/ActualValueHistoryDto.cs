namespace Application.DTOS.KpiActual
{
    public class ActualValueHistoryDto
    {
        public Guid ScorecardId { get; set; }
        public Guid KpiId { get; set; }
        public string? KpiName { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }
        public string? FiscalPeriodLabel { get; set; }
        public Guid FiscalYearId { get; set; }
        public string? FiscalYearLabel { get; set; }

        /// <summary>
        /// True when the persisted links are incomplete, cyclic or ambiguous. All available
        /// revisions are still returned; the UI can warn instead of hiding rows.
        /// </summary>
        public bool HasChainIntegrityIssues { get; set; }

        public List<ActualValueDto> Revisions { get; set; } = new();
    }
}
