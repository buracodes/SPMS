using Domain.Enums;

namespace Application.DTOS.KpiActual
{
    public class ActualValueDto
    {
        public Guid Id { get; set; }
        public Guid KpiId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }
        public decimal Value { get; set; }
        public KpiDataSource DataSource { get; set; }
        public DateTime AsOfDate { get; set; }
        public string? Note { get; set; }
    }
}
