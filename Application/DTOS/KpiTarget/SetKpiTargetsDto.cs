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

        /// <summary>
        /// Editor mode the values were entered in: true when the quarterly targets were
        /// computed from the monthly ones rather than typed. Recorded on the quarter rows so
        /// the page can restore it; ignored for a non-cumulative KPI.
        /// </summary>
        public bool AutoCalculateQuarterly { get; set; }
    }
}
