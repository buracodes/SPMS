using System;
using System.Collections.Generic;

namespace Application.DTOS.Dashboard;

/// <summary>Global filter applied to every dashboard widget (fiscal year, scorecard, period).</summary>
public sealed class DashboardFilterDto
{
    public Guid? FiscalYearId { get; set; }
    public Guid? ScorecardId { get; set; }

    /// <summary>YTD | Quarter | Month | Year - the default is YTD.</summary>
    public string PeriodType { get; set; } = "Ytd";

    public int? Quarter { get; set; }
    public int? FromMonth { get; set; }
    public int? ToMonth { get; set; }

    public string? FiscalYearLabel { get; set; }
    public string? ScorecardName { get; set; }
    public string? PeriodLabel { get; set; }

    /// <summary>True when the resolved fiscal year is closed, so the UI can warn instead of offering entry.</summary>
    public bool FiscalYearClosed { get; set; }
}

public sealed class DashboardFilterOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string? Detail { get; set; }
}

    public sealed class DashboardPeriodOptionDto
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>Optional extra hint shown under the label (for example the month span of a quarter).</summary>
        public string? Detail { get; set; }

        public bool IsSelected { get; set; }
    }

/// <summary>Widget metadata driving the shared shell layout.</summary>
public sealed class DashboardWidgetDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Span { get; set; } = 2;
}

public sealed class DashboardQuickActionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? Emphasis { get; set; }
}

/// <summary>Role driven dashboard configuration returned to the shell.</summary>
public sealed class DashboardRoleContextDto
{
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Insight { get; set; } = string.Empty;

    /// <summary>OrganizationWide | AssignedOnly.</summary>
    public string DataScope { get; set; } = string.Empty;

    public bool ReadOnly { get; set; }
    public DashboardFilterDto Filter { get; set; } = new();
    public IReadOnlyList<DashboardWidgetDto> Widgets { get; set; } = Array.Empty<DashboardWidgetDto>();
    public IReadOnlyList<DashboardQuickActionDto> QuickActions { get; set; } = Array.Empty<DashboardQuickActionDto>();
    public IReadOnlyList<DashboardFilterOptionDto> FiscalYears { get; set; } = Array.Empty<DashboardFilterOptionDto>();
    public IReadOnlyList<DashboardFilterOptionDto> Scorecards { get; set; } = Array.Empty<DashboardFilterOptionDto>();
    public IReadOnlyList<DashboardPeriodOptionDto> Periods { get; set; } = Array.Empty<DashboardPeriodOptionDto>();
}

// ---------------------------------------------------------------- widget payloads

public sealed class StatCardDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Detail { get; set; }
    public string? Tone { get; set; }

    /// <summary>Text RAG label so status is never conveyed by colour alone.</summary>
    public string? RagLabel { get; set; }

    public string? Route { get; set; }
}

public sealed class StatCardWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<StatCardDto> Cards { get; set; } = Array.Empty<StatCardDto>();
}

public sealed class GaugeWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Value { get; set; }
    public string Unit { get; set; } = "%";
    public string? RagLabel { get; set; }
    public string? Caption { get; set; }
    public string? Footnote { get; set; }
}

public sealed class RagDistributionWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Green { get; set; }
    public int Amber { get; set; }
    public int Red { get; set; }
    public int NoData { get; set; }
    public int Total { get; set; }
}

/// <summary>One plotted series; used for trends, comparisons and milestone progress.</summary>
public sealed class WidgetSeriesDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "line";
    public string? Color { get; set; }
    public IReadOnlyList<decimal?> Values { get; set; } = Array.Empty<decimal?>();
}

public sealed class TrendWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
    public IReadOnlyList<WidgetSeriesDto> Series { get; set; } = Array.Empty<WidgetSeriesDto>();
    public string ValueSuffix { get; set; } = "%";
    public string? EmptyMessage { get; set; }
}

/// <summary>
/// Comparison chart. Reuses <see cref="WidgetSeriesDto"/> so a single payload covers a
/// single ranked series (goal or division scores) and a grouped comparison (target vs
/// actual, budget by priority).
/// </summary>
public sealed class BarChartWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Category labels, already ordered by the server so the chart needs no sorting.</summary>
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    public IReadOnlyList<WidgetSeriesDto> Series { get; set; } = Array.Empty<WidgetSeriesDto>();

    /// <summary>horizontal = ranked comparisons, vertical = grouped period comparisons.</summary>
    public string Orientation { get; set; } = "horizontal";

    public string ValueSuffix { get; set; } = string.Empty;

    /// <summary>number | percent | money - drives axis and tooltip formatting only.</summary>
    public string ValueFormat { get; set; } = "number";

    /// <summary>Pin the value axis, used for 0-100 scores.</summary>
    public decimal? AxisMin { get; set; }

    public decimal? AxisMax { get; set; }

    /// <summary>
    /// Per category bar colour for a single series chart (RAG banding on scores).
    /// Ignored when more than one series is present.
    /// </summary>
    public IReadOnlyList<string> CategoryColors { get; set; } = Array.Empty<string>();

    /// <summary>Optional drill-down route per category, aligned with <see cref="Categories"/>.</summary>
    public IReadOnlyList<string?> Routes { get; set; } = Array.Empty<string?>();

    /// <summary>Headline numbers shown above the chart (period target, attainment, coverage).</summary>
    public IReadOnlyList<StatCardDto> Cards { get; set; } = Array.Empty<StatCardDto>();

    public string EmptyMessage { get; set; } = "No records for the selected filters.";
}

public sealed class TableColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>left | right | center</summary>
    public string Align { get; set; } = "left";

    /// <summary>text | number | decimal | percent | score | rag | date | status | money | priority</summary>
    public string Format { get; set; } = "text";

    public int? Width { get; set; }
}

public sealed class TableRowDto
{
    public string? Id { get; set; }
    public IReadOnlyDictionary<string, object?> Cells { get; set; } = new Dictionary<string, object?>();

    /// <summary>Optional drill-down route for the whole row.</summary>
    public string? Route { get; set; }
}

public sealed class TableWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<TableColumnDto> Columns { get; set; } = Array.Empty<TableColumnDto>();
    public IReadOnlyList<TableRowDto> Rows { get; set; } = Array.Empty<TableRowDto>();
    public string EmptyMessage { get; set; } = "No records for the selected filters.";
    public bool ShowRowLinks { get; set; }
}

public sealed class AlertDto
{
    public string Key { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Route { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public sealed class AlertListWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<AlertDto> Alerts { get; set; } = Array.Empty<AlertDto>();
    public string EmptyMessage { get; set; } = "Nothing needs attention right now.";
}

/// <summary>Stat cards plus a detail table (data entry status, budget summary, system statistics).</summary>
public sealed class CompositeWidgetDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<StatCardDto> Cards { get; set; } = Array.Empty<StatCardDto>();
    public IReadOnlyList<TableColumnDto> Columns { get; set; } = Array.Empty<TableColumnDto>();
    public IReadOnlyList<TableRowDto> Rows { get; set; } = Array.Empty<TableRowDto>();
    public string EmptyMessage { get; set; } = "No records for the selected filters.";
}

/// <summary>
/// The role scoped payload. Only the widgets present in
/// <see cref="DashboardRoleContextDto.Widgets"/> carry data, and every payload is already
/// filtered by the caller's permissions and data scope server side.
/// </summary>
public sealed class DashboardWidgetBundleDto
{
    public string Role { get; set; } = string.Empty;
    public string DataScope { get; set; } = string.Empty;
    public DashboardFilterDto Filter { get; set; } = new();
    public IReadOnlyList<DashboardWidgetDto> Widgets { get; set; } = Array.Empty<DashboardWidgetDto>();

    public StatCardWidgetDto? Summary { get; set; }
    public GaugeWidgetDto? OverallScore { get; set; }
    public RagDistributionWidgetDto? RagDistribution { get; set; }
    public TrendWidgetDto? PerformanceTrend { get; set; }
    public TableWidgetDto? ScorecardPerformance { get; set; }
    public TableWidgetDto? ScorecardHealth { get; set; }
    public TableWidgetDto? GoalHealth { get; set; }
    public TableWidgetDto? GoalPerformance { get; set; }
    public TableWidgetDto? TopBottomKpis { get; set; }
    public TableWidgetDto? KpiExceptions { get; set; }
    public TableWidgetDto? StrategicAlignment { get; set; }
    public TableWidgetDto? InitiativeStatus { get; set; }
    public TrendWidgetDto? MilestoneTrend { get; set; }
    public CompositeWidgetDto? BudgetSummary { get; set; }
    public TableWidgetDto? DivisionComparison { get; set; }
    public BarChartWidgetDto? TargetVsActual { get; set; }
    public BarChartWidgetDto? GoalPerformanceChart { get; set; }
    public BarChartWidgetDto? DivisionPerformanceChart { get; set; }
    public BarChartWidgetDto? InitiativeDeliveryChart { get; set; }
    public BarChartWidgetDto? BudgetAllocationChart { get; set; }
    public TableWidgetDto? AssignedKpis { get; set; }
    public CompositeWidgetDto? DataEntryStatus { get; set; }
    public TableWidgetDto? PeriodLocks { get; set; }
    public CompositeWidgetDto? SystemStats { get; set; }
    public TableWidgetDto? AuditActivity { get; set; }
    public TableWidgetDto? ReportsSummary { get; set; }
    public AlertListWidgetDto? Alerts { get; set; }
}

/// <summary>Goal drill-down used by the Power User goal health widget.</summary>
public sealed class GoalKraDto
{
    public Guid KraId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Weight { get; set; }
    public decimal? Score { get; set; }
    public string? RagLabel { get; set; }
    public int KpiCount { get; set; }
    public int KpiWithDataCount { get; set; }
    public decimal? Trend { get; set; }
    public IReadOnlyList<GoalKraKpiDto> Kpis { get; set; } = Array.Empty<GoalKraKpiDto>();
}

public sealed class GoalKraKpiDto
{
    public Guid KpiId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string OptimizationType { get; set; } = string.Empty;
    public decimal? Weight { get; set; }
    public decimal? Target { get; set; }
    public decimal? Actual { get; set; }
    public decimal? Score { get; set; }
    public string? RagLabel { get; set; }
    public int KpiType { get; set; }
    public bool HasData { get; set; }
    public string? Note { get; set; }
}

public sealed class GoalDrilldownDto
{
    public Guid GoalId { get; set; }
    public string GoalName { get; set; } = string.Empty;
    public decimal? Weight { get; set; }
    public decimal? Score { get; set; }
    public string? RagLabel { get; set; }
    public decimal? Trend { get; set; }
    public IReadOnlyList<GoalKraDto> Kras { get; set; } = Array.Empty<GoalKraDto>();
}
