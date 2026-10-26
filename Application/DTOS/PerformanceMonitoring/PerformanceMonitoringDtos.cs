using Application.DTOS.Division;
using System.Text.Json.Serialization;

namespace Application.DTOS.PerformanceMonitoring;

public enum PerformanceScope
{
    Corporate = 0,
    Division = 1
}

public class PerformanceMonitoringFilter
{
    public Guid? FiscalYearId { get; set; }
    public Guid? ScorecardId { get; set; }
    public Guid? DivisionId { get; set; }
    public string? PeriodType { get; set; }
    public int? Quarter { get; set; }
    public int? FromMonth { get; set; }
    public int? ToMonth { get; set; }
    public int? Year { get; set; }
}

public class PerformanceKpiRowDto
{
    public Guid KpiId { get; set; }
    public string KpiName { get; set; } = string.Empty;
    public string? KpiCode { get; set; }
    public string? Unit { get; set; }
    public decimal Weight { get; set; }
    public string? GoalName { get; set; }
    public string? KraName { get; set; }
    public string? OwnerName { get; set; }
    public string? DivisionOwnerName { get; set; }
    public decimal? Target { get; set; }
    public decimal? Actual { get; set; }
    public decimal? Score { get; set; }
    public string? Rag { get; set; }
    public string? Trend { get; set; }
}

public class PerformanceDivisionSummaryDto
{
    public Guid DivisionId { get; set; }
    public string DivisionName { get; set; } = string.Empty;
    public string? DivisionCode { get; set; }
    public decimal? OverallScore { get; set; }
    public string? Rag { get; set; }
    public int KpiCount { get; set; }
    public int GoalsCount { get; set; }
    public int GreenCount { get; set; }
    public int AmberCount { get; set; }
    public int RedCount { get; set; }
    public int NoDataCount { get; set; }
    public string? PlanStatus { get; set; }
}

public class PerformanceMonitoringDto
{
    public PerformanceScope Scope { get; set; }
    public Guid? FiscalYearId { get; set; }
    public string? FiscalYearName { get; set; }
    public Guid? ScorecardId { get; set; }
    public string? ScorecardName { get; set; }
    public Guid? DivisionId { get; set; }
    public string? DivisionName { get; set; }
    public decimal? OverallScore { get; set; }
    public string? Rag { get; set; }
    public string PeriodType { get; set; } = "Ytd";
    public int ScorecardCount { get; set; }
    public int GoalsCount { get; set; }
    public int KpiCount { get; set; }
    public int GreenCount { get; set; }
    public int AmberCount { get; set; }
    public int RedCount { get; set; }
    public int NoDataCount { get; set; }
    public IReadOnlyList<PerformanceKpiRowDto> Kpis { get; set; } = Array.Empty<PerformanceKpiRowDto>();
    public IReadOnlyList<PerformanceDivisionSummaryDto> Divisions { get; set; } = Array.Empty<PerformanceDivisionSummaryDto>();
    public IReadOnlyList<DivisionFiscalPeriodDto> FiscalPeriods { get; set; } = Array.Empty<DivisionFiscalPeriodDto>();
}
