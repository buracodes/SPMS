namespace Application.DTOS.Division
{
    /// <summary>
    /// Minimal fiscal-period reference exposed with a division plan so the target-entry form can
    /// map a fiscal month / quarter to its persisted period id (FR-904).
    /// </summary>
    public class DivisionFiscalPeriodDto
    {
        public Guid Id { get; set; }
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }
        public bool IsQuarterEnd { get; set; }
        public bool IsYearEnd { get; set; }
    }
}
