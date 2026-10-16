namespace Application.DTOS.KpiActual
{
    public class ActualValueHistoryDto
    {
        public Guid KpiId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public int FiscalMonth { get; set; }
        public List<ActualValueDto> Revisions { get; set; } = new();
    }
}
