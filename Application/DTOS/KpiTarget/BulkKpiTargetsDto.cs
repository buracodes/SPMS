using System.Text.Json.Serialization;
using Domain.Entities;

namespace Application.DTOS.KpiTarget
{
    /// <summary>
    /// Every target of one KPI for one fiscal year, used by the filtered Targets page so
    /// a whole scorecard (or fiscal year) is loaded in a single request.
    /// </summary>
    public class KpiTargetsBundleDto
    {
        public Guid KpiId { get; set; }
        public Guid FiscalYearId { get; set; }
        public List<KpiTargetValueDto> Targets { get; set; } = new();
    }

    public class GetKpiTargetsBulkDto
    {
        public Guid FiscalYearId { get; set; }

        /// <summary>Null when the request was scoped to a whole fiscal year rather than one scorecard.</summary>
        public Guid? ScorecardId { get; set; }

        public List<KpiTargetsBundleDto> Kpis { get; set; } = new();
    }

    public class SetKpiTargetsItemDto
    {
        public Guid KpiId { get; set; }
        public List<decimal> MonthlyTargets { get; set; } = new();
        public List<decimal>? QuarterlyTargets { get; set; }
        public decimal? AnnualTarget { get; set; }
    }

    public class SetKpiTargetsBulkDto
    {
        public Guid FiscalYearId { get; set; }
        public List<SetKpiTargetsItemDto> Targets { get; set; } = new();
    }

    /// <summary>Outcome of one KPI inside a bulk save, so a single bad row cannot hide the rest.</summary>
    public class KpiTargetSaveResultDto
    {
        public Guid KpiId { get; set; }
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    public class SetKpiTargetsBulkResultDto
    {
        public int SavedCount { get; set; }
        public int FailedCount { get; set; }
        public List<KpiTargetSaveResultDto> Results { get; set; } = new();
    }
}
