using Application.DTOS.Dashboard;
using Application.Dashboard;
using Domain.Entities;
using Domain.Enums;
using System.Globalization;

namespace Application.Services;

/// <summary>
/// Comparison chart builders. These turn the same period scoped data the tables use into
/// ranked bars, so the dashboard can describe performance rather than only list it.
/// Every value comes from the calculation engine, never from a formula invented here.
/// </summary>
public sealed partial class RoleDashboardService
{
    /// <summary>Above this many bars a chart stops being readable, so the tail is dropped.</summary>
    private const int MaxChartCategories = 12;

    private static readonly string[] PriorityOrder =
    {
        nameof(InitiativePriority.Low),
        nameof(InitiativePriority.Medium),
        nameof(InitiativePriority.High),
        nameof(InitiativePriority.Critical)
    };

    /// <summary>Division results shared by the division table and the division chart.</summary>
    private sealed record DivisionScoreEntry(
        string Name,
        decimal? Score,
        string PlanStatus,
        int DivisionGoals,
        int Kpis,
        int KpisWithData,
        string Route);

    /// <summary>
    /// Target against actual for the selected period. Targets and actuals are read from the
    /// same <see cref="KpiScorePeriod"/>, so the period boundaries are identical on both
    /// sides and an annual target is never spread across a partial year. KPIs are grouped by
    /// unit because summing a currency against a percentage is meaningless.
    /// </summary>
    private static BarChartWidgetDto BuildTargetVsActual(DashboardLoad load)
    {
        var units = new List<string>();
        var targetByUnit = new List<decimal>();
        var actualByUnit = new List<decimal?>();
        var colorByUnit = new List<string>();

        var rows = new List<(string Unit, decimal Target, decimal? Actual)>();

        foreach (var kpi in load.Kpis)
        {
            var period = KpiPeriod(load, kpi.Id);
            if (period?.PeriodTarget is null)
            {
                continue;
            }

            var unit = string.IsNullOrWhiteSpace(kpi.Unit) ? "No unit" : kpi.Unit.Trim();
            rows.Add((unit, period.PeriodTarget.Value, period.PeriodActual));
        }

        foreach (var group in rows
            .GroupBy(r => r.Unit, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(r => r.Target))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            units.Add(group.Key);

            // A unit is only comparable once at least one target exists for it.
            targetByUnit.Add(Math.Round(group.Sum(r => r.Target), 2));

            var withActual = group.Where(r => r.Actual.HasValue).ToList();
            actualByUnit.Add(withActual.Count == 0 ? null : Math.Round(withActual.Sum(r => r.Actual!.Value), 2));

            // Green when the unit cleared its target, amber when some actuals are missing.
            colorByUnit.Add(
                withActual.Count == 0
                    ? "slate"
                    : withActual.Sum(r => r.Actual!.Value) >= group.Sum(r => r.Target)
                        ? "Green"
                        : withActual.Count < group.Count()
                            ? "Amber"
                            : "Red");
        }

        var targetCount = rows.Count;
        var withActualCount = rows.Count(r => r.Actual.HasValue);
        var onTrack = load.Kpis.Count(k => load.Rag(KpiPeriod(load, k.Id)?.PeriodScore) == DashboardRag.Green);
        var offTrack = load.Kpis.Count(k => load.RagIsRed(KpiPeriod(load, k.Id)?.PeriodScore));

        return new BarChartWidgetDto
        {
            Title = "Target vs actual",
            Description =
                $"Configured targets against entered actuals for the same {ScopeText(load).ToLowerInvariant()} period. " +
                "KPIs are grouped by unit because different units cannot be added together.",
            Categories = units,
            Series = new[]
            {
                new WidgetSeriesDto { Name = "Target", Kind = "bar", Color = "slate", Values = targetByUnit.Cast<decimal?>().ToList() },
                new WidgetSeriesDto { Name = "Actual", Kind = "bar", Color = "accent", Values = actualByUnit }
            },
            Orientation = "horizontal",
            ValueFormat = "number",
            CategoryColors = colorByUnit,
            Cards = new[]
            {
                new StatCardDto { Key = "targets", Label = "KPIs with a target", Value = targetCount.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto { Key = "actuals", Label = "Actuals entered", Value = withActualCount.ToString(CultureInfo.InvariantCulture),
                    Detail = targetCount == 0 ? null : $"{Math.Round((decimal)withActualCount / targetCount * 100m)}% of targeted KPIs" },
                new StatCardDto { Key = "onTrack", Label = "KPIs on track", Value = onTrack.ToString(CultureInfo.InvariantCulture), RagLabel = DashboardRag.Green },
                new StatCardDto { Key = "offTrack", Label = "KPIs off track", Value = offTrack.ToString(CultureInfo.InvariantCulture), RagLabel = offTrack > 0 ? DashboardRag.Red : DashboardRag.Green },
                new StatCardDto { Key = "average", Label = "Average KPI score", Value = Percent(AverageScore(load)), Unit = "%" }
            },
            EmptyMessage = "No targets are configured for this scorecard and period."
        };
    }

    /// <summary>Goals ranked by weighted achievement, coloured by the same RAG bands used elsewhere.</summary>
    private static BarChartWidgetDto BuildGoalPerformanceChart(DashboardLoad load)
    {
        var ranked = load.Goals
            .Select(goal => (Goal: goal, Score: GoalScore(load, goal.Id)))
            .OrderByDescending(x => x.Score ?? decimal.MinValue)
            .ThenBy(x => x.Goal.Name, StringComparer.Ordinal)
            .ToList();

        var shown = ranked.Take(MaxChartCategories).ToList();
        var scorecardId = load.SelectedScorecard?.Id;

        return new BarChartWidgetDto
        {
            Title = "Goal achievement",
            Description = ranked.Count > shown.Count
                ? $"Top {shown.Count} of {ranked.Count} goals by weighted achievement for the selected period."
                : "Every goal ranked by weighted achievement for the selected period.",
            Categories = shown.Select(x => x.Goal.Name).ToList(),
            Series = new[]
            {
                new WidgetSeriesDto
                {
                    Name = "Achievement",
                    Kind = "bar",
                    Color = "primary",
                    Values = shown.Select(x => x.Score).ToList()
                }
            },
            Orientation = "horizontal",
            ValueFormat = "percent",
            ValueSuffix = "%",
            AxisMin = 0m,
            AxisMax = 100m,
            CategoryColors = shown.Select(x => load.Rag(x.Score)).ToList(),
            Routes = scorecardId is null
                ? new string?[] { }
                : shown.Select(x => $"/scorecards/{scorecardId}?goalId={x.Goal.Id}").ToArray(),
            EmptyMessage = "No goals match the selected filters."
        };
    }

    /// <summary>Divisions ranked by plan score, sharing the calculation the division table uses.</summary>
    private static BarChartWidgetDto BuildDivisionPerformanceChart(
        DashboardLoad load,
        IReadOnlyList<DivisionScoreEntry> divisions)
    {
        var ranked = divisions
            .OrderByDescending(d => d.Score ?? decimal.MinValue)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        var shown = ranked.Take(MaxChartCategories).ToList();

        return new BarChartWidgetDto
        {
            Title = "Division performance",
            Description = ranked.Count > shown.Count
                ? $"Top {shown.Count} of {ranked.Count} divisions by weighted plan score."
                : "Divisions ranked by weighted plan score for the selected period.",
            Categories = shown.Select(d => d.Name).ToList(),
            Series = new[]
            {
                new WidgetSeriesDto
                {
                    Name = "Plan score",
                    Kind = "bar",
                    Color = "primary",
                    Values = shown.Select(d => d.Score).ToList()
                }
            },
            Orientation = "horizontal",
            ValueFormat = "percent",
            ValueSuffix = "%",
            AxisMin = 0m,
            AxisMax = 100m,
            CategoryColors = shown.Select(d => load.Rag(d.Score)).ToList(),
            Routes = shown.Select(d => d.Route).ToList(),
            Cards = new[]
            {
                new StatCardDto { Key = "divisions", Label = "Divisions in plan", Value = ranked.Count.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto
                {
                    Key = "withData",
                    Label = "Divisions with KPI data",
                    Value = ranked.Count(d => d.KpisWithData > 0).ToString(CultureInfo.InvariantCulture)
                }
            },
            EmptyMessage = "No division plans exist for this scorecard."
        };
    }

    /// <summary>
    /// Milestone delivery for the active initiatives that carry the most budget, so the chart
    /// answers "where is the money going and how far along is it".
    /// </summary>
    private BarChartWidgetDto BuildInitiativeDeliveryChart(DashboardLoad load)
    {
        var active = load.Initiatives
            .Where(i => i.Status is InitiativeStatus.NotStarted or InitiativeStatus.InProgress)
            .OrderByDescending(i => i.Budget)
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ToList();

        var shown = active.Take(MaxChartCategories).ToList();

        return new BarChartWidgetDto
        {
            Title = "Initiative delivery",
            Description = shown.Count < active.Count
                ? $"Milestone delivery for the {shown.Count} highest budget active initiatives out of {active.Count}."
                : "Weighted milestone delivery for every active initiative.",
            Categories = shown.Select(i => i.Name).ToList(),
            Series = new[]
            {
                new WidgetSeriesDto
                {
                    Name = "Delivery",
                    Kind = "bar",
                    Color = "primary",
                    Values = shown.Select(i => (decimal?)InitiativeProgress(i)).ToList()
                }
            },
            Orientation = "horizontal",
            ValueFormat = "percent",
            ValueSuffix = "%",
            AxisMin = 0m,
            AxisMax = 100m,
            CategoryColors = shown.Select(i => load.Rag(InitiativeProgress(i))).ToList(),
            Routes = shown.Select(i => $"/initiatives/{i.Id}").ToList(),
            EmptyMessage = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                ? "You do not own any active initiatives."
                : "No active initiatives with milestones to track."
        };
    }

    /// <summary>Planned budget split by priority; the status split already lives in the budget table.</summary>
    private static BarChartWidgetDto BuildBudgetAllocationChart(DashboardLoad load)
    {
        var byPriority = load.Initiatives
            .GroupBy(i => i.Priority)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Budget));

        var total = load.Initiatives.Sum(i => i.Budget);

        return new BarChartWidgetDto
        {
            Title = "Budget allocation",
            Description = "Planned budget grouped by priority so spend concentration is visible at a glance.",
            Categories = PriorityOrder
                .Select(p => Enum.TryParse<InitiativePriority>(p, out var parsed) ? parsed.ToString() : p)
                .ToList(),
            Series = new[]
            {
                new WidgetSeriesDto
                {
                    Name = "Planned budget",
                    Kind = "bar",
                    Color = "primary",
                    Values = PriorityOrder
                        .Select(p => byPriority.TryGetValue(Enum.Parse<InitiativePriority>(p), out var amount)
                            ? (decimal?)amount
                            : null)
                        .ToList()
                }
            },
            Orientation = "vertical",
            ValueFormat = "money",
            CategoryColors = new[] { "slate", "blue", "accent", "primary" },
            Cards = new[]
            {
                new StatCardDto { Key = "total", Label = "Total planned budget", Value = Money(total) },
                new StatCardDto { Key = "initiatives", Label = "Funded initiatives", Value = load.Initiatives.Count.ToString(CultureInfo.InvariantCulture) }
            },
            EmptyMessage = "No initiative budgets are available."
        };
    }
}
