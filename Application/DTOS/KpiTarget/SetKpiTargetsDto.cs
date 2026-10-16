using Domain.Entities;

namespace Application.DTOS.KpiTarget
{
    public class SetKpiTargetsDto
    {
        public Guid KpiId { get; set; }
        public Guid FiscalYearId { get; set; }

        public List<decimal> MonthlyTargets { get; set; } = new();

        public List<decimal>? QuarterlyTargets { get; set; }

        public decimal? AnnualTarget { get; set; }
    }
}
