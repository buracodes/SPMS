using Application.DTOS.Dashboard;
using Application.Dashboard;
using Application.Interface.IService;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Domain.Calculation;
using System.Globalization;

namespace Application.Services;

public sealed partial class RoleDashboardService
{
    // ------------------------------------------------------------------ shared helpers

    private static KpiScorePeriod? KpiPeriod(DashboardLoad load, Guid kpiId)
        => load.SelectedPeriodResult is not null && load.SelectedPeriodResult.Kpis.TryGetValue(kpiId, out var period)
            ? period
            : null;

    private static decimal? GoalScore(DashboardLoad load, Guid goalId)
        => load.SelectedPeriodResult is not null && load.SelectedPeriodResult.Goals.TryGetValue(goalId, out var goal)
            ? goal.PeriodScore
            : null;

    private static decimal? KraScore(DashboardLoad load, Guid kraId)
        => load.SelectedPeriodResult is not null && load.SelectedPeriodResult.Kras.TryGetValue(kraId, out var kra)
            ? kra.PeriodScore
            : null;

    private static decimal? OverallScore(DashboardLoad load)
    {
        var scorecardId = load.SelectedScorecard?.Id;
        if (scorecardId is null)
        {
            return null;
        }

        return load.SelectedPeriodResult is not null
            && load.SelectedPeriodResult.Scorecards.TryGetValue(scorecardId.Value, out var entity)
            ? entity.PeriodScore
            : null;
    }

    /// <summary>Movement between the last two months that have a score, used for the trend column.</summary>
    private static decimal? TrendOf(IReadOnlyDictionary<int, decimal?> monthly)
    {
        var points = monthly
            .Where(kv => kv.Value.HasValue)
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value!.Value)
            .ToList();

        if (points.Count < 2)
        {
            return null;
        }

        return Math.Round(points[^1] - points[^2], 1);
    }

    private static string Percent(decimal? value)
        => value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-";

    private static string Signed(decimal? value)
        => value.HasValue ? (value.Value > 0 ? "+" : string.Empty) + value.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-";

    private static string Money(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Date(DateTime? value)
        => value.HasValue ? value.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "-";

    private static string OptimisationText(KpiOptimizationType type) => type switch
    {
        KpiOptimizationType.Maximize => "Higher is better",
        KpiOptimizationType.Minimize => "Lower is better",
        KpiOptimizationType.ExactHit => "On target",
        KpiOptimizationType.Binary => "Met / not met",
        _ => "-"
    };

    private static string GoalName(DashboardLoad load, Guid? goalId)
    {
        if (goalId is null)
        {
            return "-";
        }

        return load.Goals.FirstOrDefault(g => g.Id == goalId.Value)?.Name ?? "-";
    }

    /// <summary>FRD lifecycle labels. Archived was retired; an initiative nobody is working on is On hold.</summary>
    private static string InitiativeStatusText(InitiativeStatus status) => status switch
    {
        InitiativeStatus.NotStarted => "Not started",
        InitiativeStatus.InProgress => "In progress",
        InitiativeStatus.Completed => "Completed",
        InitiativeStatus.OnHold => "On hold",
        InitiativeStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    private static string InitiativeStatusTone(InitiativeStatus status) => status switch
    {
        InitiativeStatus.Completed => "success",
        InitiativeStatus.InProgress => "primary",
        InitiativeStatus.NotStarted => "info",
        InitiativeStatus.OnHold => "warning",
        InitiativeStatus.Cancelled => "danger",
        _ => "muted"
    };

    /// <summary>
    /// FR-403 progress. Delegates to <see cref="IInitiativeProgressCalculator"/> so dashboards, the
    /// grid, reports and the API all agree on one definition.
    /// </summary>
    private int InitiativeProgress(Initiative initiative)
    {
        var result = initiativeProgressCalculator.Calculate(
            initiative.Milestones?
                .Select(m => new InitiativeProgressInput(m.Id, m.Weight, m.CompletionPercentage))
                ?? Enumerable.Empty<InitiativeProgressInput>());

        if (!result.HasMilestones)
        {
            // No milestones means nothing is being tracked; only a completed initiative reads 100.
            return initiative.Status == InitiativeStatus.Completed ? 100 : 0;
        }

        return (int)Math.Round(result.ProgressPercentage, MidpointRounding.AwayFromZero);
    }

    private static TableRowDto Row(string? id, string? route, params (string Key, object? Value)[] cells)
        => new()
        {
            Id = id,
            Route = route,
            Cells = cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase)
        };

    private static TableColumnDto Col(string key, string label, string format = "text", string align = "left")
        => new() { Key = key, Label = label, Format = format, Align = align };

    private static string? NoteFor(DashboardLoad load, Guid kpiId, int fiscalMonth)
    {
        var note = load.Actuals
            .Where(a => a.KpiId == kpiId && a.FiscalPeriod?.FiscalMonth == fiscalMonth)
            .OrderByDescending(a => a.AsOfDate)
            .Select(a => a.Note)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        return string.IsNullOrWhiteSpace(note) ? null : note;
    }

    // ------------------------------------------------------------------ summary cards

    private async Task<StatCardWidgetDto> BuildSummaryAsync(DashboardLoad load, CancellationToken cancellationToken)
    {
        var score = OverallScore(load);
        var rag = DashboardRag.FromScore(score);
        var goalsWithScore = load.Goals.Count(g => GoalScore(load, g.Id).HasValue);
        var kpisWithData = load.Kpis.Count(k => KpiPeriod(load, k.Id)?.PeriodScore.HasValue == true);
        var currentMonth = load.CurrentFiscalMonth;

        var cards = new List<StatCardDto>
        {
            new()
            {
                Key = "score",
                Label = load.Profile.DataScope == DashboardDataScope.AssignedOnly ? "My KPI average" : "Overall score",
                Value = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                    ? Percent(AverageScore(load))
                    : Percent(score),
                Unit = "%",
                RagLabel = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                    ? DashboardRag.FromScore(AverageScore(load))
                    : rag,
                Detail = load.FiscalYear is null
                    ? null
                    : $"{load.FiscalYear.Label} - {(load.SelectedPeriodResult?.Scope is null ? "YTD" : ScopeText(load))}"
            }
        };

        if (load.Has(DashboardRoleCatalog.WidgetAssignedKpis) || load.Profile.DataScope == DashboardDataScope.AssignedOnly)
        {
            cards.Add(new StatCardDto
            {
                Key = "assignedKpis",
                Label = "KPIs assigned to me",
                Value = load.Kpis.Count.ToString(CultureInfo.InvariantCulture),
                Detail = $"{kpisWithData} with data for the selected period"
            });
        }
        else
        {
            cards.Add(new StatCardDto
            {
                Key = "goals",
                Label = "Goals on track",
                Value = load.Goals.Count(g => DashboardRag.FromScore(GoalScore(load, g.Id)) == DashboardRag.Green)
                    .ToString(CultureInfo.InvariantCulture),
                Detail = $"{goalsWithScore} of {load.Goals.Count} scored"
            });

            cards.Add(new StatCardDto
            {
                Key = "kpis",
                Label = "KPIs on track",
                Value = load.Kpis.Count(k => DashboardRag.FromScore(KpiPeriod(load, k.Id)?.PeriodScore) == DashboardRag.Green)
                    .ToString(CultureInfo.InvariantCulture),
                Detail = $"{kpisWithData} of {load.Kpis.Count} with data"
            });
        }

        if (load.Initiatives.Count > 0)
        {
            cards.Add(new StatCardDto
            {
                Key = "initiatives",
                Label = "Initiatives in progress",
                Value = load.Initiatives.Count(i => i.Status == InitiativeStatus.InProgress).ToString(CultureInfo.InvariantCulture),
                Detail = $"{load.Initiatives.Count(i => i.Status == InitiativeStatus.Completed)} completed"
            });
        }

        var missing = load.Kpis.Count(k => KpiPeriod(load, k.Id)?.PeriodActual == null);
        if (load.Has(DashboardRoleCatalog.WidgetDataEntryStatus) || load.Profile.DataScope == DashboardDataScope.AssignedOnly)
        {
            cards.Add(new StatCardDto
            {
                Key = "missing",
                Label = "KPIs missing actuals",
                Value = missing.ToString(CultureInfo.InvariantCulture),
                Tone = missing > 0 ? "warning" : "success",
                Detail = currentMonth > 0
                    ? $"For {MonthLabel(load.FiscalYear, currentMonth)}"
                    : "No active period"
            });
        }

        cards.Add(new StatCardDto
        {
            Key = "scorecards",
            Label = "Scorecards",
            Value = load.Scorecards.Count.ToString(CultureInfo.InvariantCulture),
            Detail = $"{load.Scorecards.Count(s => s.ScorecardStatus == ScorecardStatus.Active)} active",
            Route = "/scorecards"
        });

        return new StatCardWidgetDto
        {
            Title = "Performance summary",
            Description = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                ? "Your assigned KPIs for the selected fiscal year, scorecard and period."
                : "Headline numbers for the selected fiscal year, scorecard and period.",
            Cards = cards
        };
    }

    private static string ScopeText(DashboardLoad load)
    {
        var scope = load.SelectedPeriodResult?.Scope;

        if (scope is null)
        {
            return "YTD";
        }

        return scope.Granularity switch
        {
            SnapshotGranularity.Quarter => $"Q{scope.Quarter}",
            SnapshotGranularity.Month => MonthLabel(load.FiscalYear, scope.FromMonth),
            SnapshotGranularity.Year => "Full year",
            _ => "YTD"
        };
    }

    private static decimal? AverageScore(DashboardLoad load)
    {
        var values = load.Kpis
            .Select(k => KpiPeriod(load, k.Id)?.PeriodScore)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        return values.Count == 0 ? null : Math.Round(values.Average(), 1);
    }

    private GaugeWidgetDto BuildOverallScore(DashboardLoad load)
    {
        var score = load.Profile.DataScope == DashboardDataScope.AssignedOnly ? AverageScore(load) : OverallScore(load);

        return new GaugeWidgetDto
        {
            Title = "Overall score",
            Description = "Weighted score produced by the SPMS calculation engine.",
            Value = score,
            Unit = "%",
            RagLabel = DashboardRag.FromScore(score),
            Caption = score.HasValue ? DashboardRag.FromScore(score) switch
            {
                DashboardRag.Green => "On track",
                DashboardRag.Amber => "At risk",
                DashboardRag.Red => "Off track",
                _ => "No data yet"
            } : "No data yet",
            Footnote = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                ? "Average of the KPIs assigned to you."
                : load.SelectedScorecard?.Name
        };
    }

    private RagDistributionWidgetDto BuildRagDistribution(DashboardLoad load)
    {
        var green = 0;
        var amber = 0;
        var red = 0;
        var noData = 0;

        foreach (var kpi in load.Kpis)
        {
            switch (DashboardRag.FromScore(KpiPeriod(load, kpi.Id)?.PeriodScore))
            {
                case DashboardRag.Green: green++; break;
                case DashboardRag.Amber: amber++; break;
                case DashboardRag.Red: red++; break;
                default: noData++; break;
            }
        }

        return new RagDistributionWidgetDto
        {
            Title = "KPI RAG distribution",
            Description = "On track, at risk, off track and KPIs still without data.",
            Green = green,
            Amber = amber,
            Red = red,
            NoData = noData,
            Total = load.Kpis.Count
        };
    }

    // ------------------------------------------------------------------ charts

    private TrendWidgetDto BuildPerformanceTrend(DashboardLoad load)
    {
        var year = load.SelectedYearResult;
        var scorecardId = load.SelectedScorecard?.Id;
        var months = load.Periods
            .Where(p => load.CurrentFiscalMonth <= 0 || p.FiscalMonth <= load.CurrentFiscalMonth)
            .OrderBy(p => p.FiscalMonth)
            .ToList();

        if (year is null || scorecardId is null || months.Count == 0)
        {
            return new TrendWidgetDto
            {
                Title = "Performance trend",
                Description = "Month by month movement for the selected fiscal year.",
                EmptyMessage = "No score history is available for this scorecard yet."
            };
        }

        var scorecardScores = year.Scorecards.TryGetValue(scorecardId.Value, out var entity)
            ? entity.MonthlyScores
            : new Dictionary<int, decimal?>();

        var kpiAverages = new Dictionary<int, decimal?>();
        foreach (var period in months)
        {
            var month = period.FiscalMonth;
            var values = load.Kpis
                .Select(k => year.Kpis.TryGetValue(k.Id, out var p) && p.MonthlyScores.TryGetValue(month, out var m) ? m : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            kpiAverages[month] = values.Count == 0 ? null : Math.Round(values.Average(), 1);
        }

        return new TrendWidgetDto
        {
            Title = "Performance trend",
            Description = "Month by month movement for the selected fiscal year.",
            Categories = months.Select(MonthLabel).ToList(),
            Series = new[]
            {
                new WidgetSeriesDto
                {
                    Name = load.SelectedScorecard?.Name ?? "Scorecard",
                    Kind = "line",
                    Color = "primary",
                    Values = months.Select(m => scorecardScores.TryGetValue(m.FiscalMonth, out var v) ? v : null).ToList()
                },
                new WidgetSeriesDto
                {
                    Name = load.Profile.DataScope == DashboardDataScope.AssignedOnly ? "My KPI average" : "KPI average",
                    Kind = "line",
                    Color = "accent",
                    Values = months.Select(m => kpiAverages.GetValueOrDefault(m.FiscalMonth)).ToList()
                }
            },
            ValueSuffix = "%",
            EmptyMessage = "No score history is available for this scorecard yet."
        };
    }

    private TrendWidgetDto BuildMilestoneTrend(DashboardLoad load)
    {
        var months = load.Periods
            .Where(p => load.CurrentFiscalMonth <= 0 || p.FiscalMonth <= load.CurrentFiscalMonth)
            .OrderBy(p => p.FiscalMonth)
            .ToList();

        var active = load.Initiatives
            .Where(i => i.Status is InitiativeStatus.NotStarted or InitiativeStatus.InProgress)
            .ToList();

        if (active.Count == 0 || months.Count == 0)
        {
            return new TrendWidgetDto
            {
                Title = "Milestone completion",
                Description = "Planned progress against delivered progress.",
                EmptyMessage = "No active initiatives with milestones to track."
            };
        }

        var now = load.NowUtc.Date;
        var actual = Math.Round(active.Average(i => (decimal)InitiativeProgress(i)), 1);

        var plannedValues = new List<decimal?>();
        var actualValues = new List<decimal?>();

        foreach (var month in months)
        {
            var mid = month.StartDate.AddDays((month.EndDate - month.StartDate).Days / 2);

            var planned = active.Sum(i =>
            {
                var start = i.StartDate;
                var end = i.EndDate;

                if (mid <= start)
                {
                    return 0m;
                }

                if (mid >= end)
                {
                    return 100m;
                }

                var total = (end - start).TotalDays;
                return total <= 0 ? 100m : Math.Round((decimal)((mid - start).TotalDays / total) * 100m, 1);
            });

            plannedValues.Add(Math.Round(planned / active.Count, 1));

            // Delivered progress is only knowable for months that have already happened.
            actualValues.Add(mid <= now ? actual : null);
        }

        return new TrendWidgetDto
        {
            Title = "Milestone completion",
            Description = "Planned progress against delivered progress across active initiatives.",
            Categories = months.Select(MonthLabel).ToList(),
            Series = new[]
            {
                new WidgetSeriesDto { Name = "Planned", Kind = "line", Color = "slate", Values = plannedValues },
                new WidgetSeriesDto { Name = "Delivered", Kind = "line", Color = "primary", Values = actualValues }
            },
            ValueSuffix = "%",
            EmptyMessage = "No active initiatives with milestones to track."
        };
    }

    // ------------------------------------------------------------------ scorecard widgets

    private async Task<TableWidgetDto> BuildScorecardPerformanceAsync(DashboardLoad load, CancellationToken cancellationToken)
    {
        var rows = new List<TableRowDto>();

        foreach (var scorecard in load.Scorecards.Take(MaxScorecardsForComparison))
        {
            var result = load.PeriodResults.TryGetValue(scorecard.Id, out var r) ? r : null;
            var entity = result is not null && result.Scorecards.TryGetValue(scorecard.Id, out var e) ? e : null;
            var score = entity?.PeriodScore;

            var goalScores = result?.Goals.Values
                .Where(g => g.PeriodScore.HasValue)
                .Select(g => g.PeriodScore!.Value)
                .ToList() ?? new List<decimal>();

            rows.Add(Row(
                scorecard.Id.ToString(),
                $"/scorecards/{scorecard.Id}",
                ("scorecard", scorecard.Name),
                ("scope", scorecard.Scope.ToString()),
                ("status", scorecard.ScorecardStatus == ScorecardStatus.Active ? "Active" : "Closed"),
                ("goals", result?.Goals.Count ?? 0),
                ("avgGoal", goalScores.Count == 0 ? "-" : Math.Round(goalScores.Average(), 1).ToString("0.0", CultureInfo.InvariantCulture)),
                ("score", Percent(score)),
                ("rag", DashboardRag.FromScore(score)),
                ("trend", Signed(entity is null ? null : TrendOf(entity.MonthlyScores)))
            ));
        }

        return new TableWidgetDto
        {
            Title = "Scorecard performance",
            Description = "Every scorecard the selected fiscal year contains, with score and RAG.",
            Columns = new[]
            {
                Col("scorecard", "Scorecard"),
                Col("scope", "Scope"),
                Col("status", "Status"),
                Col("goals", "Goals", "number", "right"),
                Col("avgGoal", "Avg goal score", "percent", "right"),
                Col("score", "Score", "percent", "right"),
                Col("rag", "RAG", "rag"),
                Col("trend", "Trend", "number", "right")
            },
            Rows = rows,
            EmptyMessage = "No scorecards are available for this fiscal year.",
            ShowRowLinks = true
        };
    }

    private async Task<TableWidgetDto> BuildScorecardHealthAsync(DashboardLoad load, CancellationToken cancellationToken)
    {
        var rows = new List<TableRowDto>();
        var initiatives = load.Initiatives.Count > 0
            ? load.Initiatives
            : await initiativeRepository.GetAllWithMilestonesAsync();

        foreach (var scorecard in load.Scorecards.Take(MaxScorecardsForComparison))
        {
            var result = load.PeriodResults.TryGetValue(scorecard.Id, out var r) ? r : null;
            var score = result is not null && result.Scorecards.TryGetValue(scorecard.Id, out var entity)
                ? entity.PeriodScore
                : null;

            var goals = await goalRepository.GetByScorecardAsync(scorecard.Id);
            var kpis = await kpiRepository.GetByScorecardAsync(scorecard.Id);
            var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecard.Id, load.FiscalYear!.Id);
            var targetKpiIds = targets.Select(t => t.KpiId).ToHashSet();
            var missingTargets = kpis.Count(k => !targetKpiIds.Contains(k.Id));
            var kras = kpis.Count(k => k.KraId.HasValue);

            var openIssues = (missingTargets > 0 ? 1 : 0)
                + (goals.Count == 0 ? 1 : 0)
                + (kpis.Count == 0 ? 1 : 0);

            rows.Add(Row(
                scorecard.Id.ToString(),
                $"/scorecards/{scorecard.Id}",
                ("scorecard", scorecard.Name),
                ("scope", scorecard.Scope.ToString()),
                ("status", scorecard.ScorecardStatus == ScorecardStatus.Active ? "Active" : "Closed"),
                ("goals", goals.Count),
                ("kras", kras),
                ("kpis", kpis.Count),
                ("missingTargets", missingTargets),
                ("initiatives", initiatives.Count),
                ("openIssues", openIssues),
                ("score", Percent(score)),
                ("rag", DashboardRag.FromScore(score))
            ));
        }

        return new TableWidgetDto
        {
            Title = "Scorecard health",
            Description = "Plan completeness per scorecard: hierarchy coverage, target configuration and open issues.",
            Columns = new[]
            {
                Col("scorecard", "Scorecard"),
                Col("scope", "Scope"),
                Col("status", "Status"),
                Col("goals", "Goals", "number", "right"),
                Col("kras", "KRAs", "number", "right"),
                Col("kpis", "KPIs", "number", "right"),
                Col("missingTargets", "KPIs without targets", "number", "right"),
                Col("initiatives", "Initiatives", "number", "right"),
                Col("openIssues", "Open issues", "number", "right"),
                Col("score", "Score", "percent", "right"),
                Col("rag", "RAG", "rag")
            },
            Rows = rows,
            EmptyMessage = "No scorecards are available for this fiscal year.",
            ShowRowLinks = true
        };
    }

    // ------------------------------------------------------------------ goal widgets

    private TableWidgetDto BuildGoalTable(DashboardLoad load, bool drillDown)
    {
        var rows = load.Goals
            .Select(goal =>
            {
                var score = GoalScore(load, goal.Id);
                var trend = load.SelectedPeriodResult is not null
                    && load.SelectedPeriodResult.Goals.TryGetValue(goal.Id, out var entity)
                        ? TrendOf(entity.MonthlyScores)
                        : null;

                var kpiCount = load.KpiCountByGoal.GetValueOrDefault(goal.Id);
                var kpiWithData = load.Kpis.Count(k =>
                    k.Kra?.GoalId == goal.Id && KpiPeriod(load, k.Id)?.PeriodScore.HasValue == true);

                return new
                {
                    Goal = goal,
                    Score = score,
                    Trend = trend,
                    KpiCount = kpiCount,
                    KpiWithData = kpiWithData
                };
            })
            .OrderByDescending(x => x.Score ?? decimal.MinValue)
            .ThenBy(x => x.Goal.Name, StringComparer.Ordinal)
            .Select(x =>
            {
                // The trend column only exists in the drill-down layout, so the cell has to
                // follow it or the row carries a key no column renders.
                var cells = new List<(string Key, object? Value)>
                {
                    ("goal", x.Goal.Name),
                    ("weight", x.Goal.Weight.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("kpis", x.KpiCount),
                    ("kpisWithData", x.KpiWithData),
                    ("score", Percent(x.Score)),
                    ("rag", DashboardRag.FromScore(x.Score))
                };

                if (drillDown)
                {
                    cells.Add(("trend", Signed(x.Trend)));
                }

                return Row(
                    x.Goal.Id.ToString(),
                    drillDown ? $"/scorecards/{load.SelectedScorecard?.Id}?goalId={x.Goal.Id}" : null,
                    cells.ToArray());
            })
            .ToList();

        var columns = new List<TableColumnDto>
        {
            Col("goal", "Goal"),
            Col("weight", "Weight %", "number", "right"),
            Col("kpis", "KPIs", "number", "right"),
            Col("kpisWithData", "With data", "number", "right"),
            Col("score", "Score", "percent", "right"),
            Col("rag", "RAG", "rag")
        };

        if (drillDown)
        {
            columns.Add(Col("trend", "Trend", "number", "right"));
        }

        return new TableWidgetDto
        {
            Title = drillDown ? "Goal health overview" : "Goal performance",
            Description = drillDown
                ? "Goal, score, RAG and trend. Select a goal to drill into its KRAs."
                : "Read-only goal score and RAG for the selected period.",
            Columns = columns,
            Rows = rows,
            EmptyMessage = "No goals match the selected filters.",
            ShowRowLinks = drillDown
        };
    }

    private GoalDrilldownDto BuildGoalDrilldown(DashboardLoad load, Goal goal)
    {
        var score = GoalScore(load, goal.Id);
        var trend = load.SelectedPeriodResult is not null
            && load.SelectedPeriodResult.Goals.TryGetValue(goal.Id, out var entity)
                ? TrendOf(entity.MonthlyScores)
                : null;

        var kras = new List<GoalKraDto>();

        var kraGroups = load.Kpis
            .Where(k => k.Kra?.GoalId == goal.Id)
            .GroupBy(k => k.Kra!.Id);

        foreach (var group in kraGroups)
        {
            var kra = group.First().Kra!;
            var kraId = kra.Id;
            var kraScore = KraScore(load, kraId);
            var krasTrend = load.SelectedPeriodResult is not null
                && load.SelectedPeriodResult.Kras.TryGetValue(kraId, out var k)
                    ? TrendOf(k.MonthlyScores)
                    : null;

            var kpiDtos = group
                .Select(kpi =>
                {
                    var period = KpiPeriod(load, kpi.Id);
                    var month = period?.MonthlyActuals.Keys.DefaultIfEmpty(0).Max() ?? 0;

                    return new GoalKraKpiDto
                    {
                        KpiId = kpi.Id,
                        Name = kpi.Name,
                        Unit = kpi.Unit ?? string.Empty,
                        OptimizationType = OptimisationText(kpi.OptimizationType),
                        Weight = kpi.Weight,
                        Target = period?.PeriodTarget,
                        Actual = period?.PeriodActual,
                        Score = period?.PeriodScore,
                        RagLabel = DashboardRag.FromScore(period?.PeriodScore),
                        KpiType = (int)kpi.KpiType,
                        HasData = period?.PeriodActual.HasValue == true,
                        Note = month > 0 ? NoteFor(load, kpi.Id, month) : null
                    };
                })
                .OrderBy(k => k.Name)
                .ToList();

            kras.Add(new GoalKraDto
            {
                KraId = kraId,
                Name = kra.Name,
                Weight = kra.Weight,
                Score = kraScore,
                RagLabel = DashboardRag.FromScore(kraScore),
                KpiCount = kpiDtos.Count,
                KpiWithDataCount = kpiDtos.Count(k => k.HasData),
                Trend = krasTrend,
                Kpis = kpiDtos
            });
        }

        return new GoalDrilldownDto
        {
            GoalId = goal.Id,
            GoalName = goal.Name,
            Weight = goal.Weight,
            Score = score,
            RagLabel = DashboardRag.FromScore(score),
            Trend = trend,
            Kras = kras
        };
    }

    // ------------------------------------------------------------------ KPI widgets

    private TableWidgetDto BuildTopBottomKpis(DashboardLoad load)
    {
        var scored = load.Kpis
            .Select(kpi => (Kpi: kpi, Period: KpiPeriod(load, kpi.Id)))
            .Where(x => x.Period?.PeriodScore.HasValue == true)
            .OrderBy(x => x.Period!.PeriodScore!.Value)
            .ToList();

        var bottom = scored.Take(TopBottomKpiCount);
        var top = scored.Skip(Math.Max(0, scored.Count - TopBottomKpiCount))
            .OrderByDescending(x => x.Period!.PeriodScore!.Value)
            .ToList();

        var rows = top.Select(x => KpiRow(load, x.Kpi, x.Period!, "Top"))
            .Concat(bottom.Select(x => KpiRow(load, x.Kpi, x.Period!, "Bottom")))
            .ToList();

        return new TableWidgetDto
        {
            Title = "Top and bottom KPIs",
            Description = "Highest and lowest performing KPIs for the selected period.",
            Columns = new[]
            {
                Col("band", "Band"),
                Col("kpi", "KPI"),
                Col("kra", "KRA"),
                Col("target", "Target", "number", "right"),
                Col("actual", "Actual", "number", "right"),
                Col("score", "Score", "percent", "right"),
                Col("rag", "RAG", "rag")
            },
            Rows = rows,
            EmptyMessage = "No scored KPIs are available for the selected period."
        };
    }

    private async Task<TableWidgetDto> BuildTopBottomKpisAsync(DashboardLoad load, CancellationToken cancellationToken)
        => await Task.FromResult(BuildTopBottomKpis(load));

    private static TableRowDto KpiRow(DashboardLoad load, Kpi kpi, KpiScorePeriod period, string band)
        => Row(
            kpi.Id.ToString(),
            null,
            ("band", band),
            ("kpi", kpi.Name),
            ("kra", kpi.Kra?.Name ?? "-"),
            ("target", period.PeriodTarget),
            ("actual", period.PeriodActual),
            ("score", Percent(period.PeriodScore)),
            ("rag", DashboardRag.FromScore(period.PeriodScore)));

    private TableWidgetDto BuildKpiExceptions(DashboardLoad load)
    {
        var rows = load.Kpis
            .Select(kpi => (Kpi: kpi, Period: KpiPeriod(load, kpi.Id)))
            .Where(x => x.Period is not null
                        && (x.Period.PeriodScore is null || DashboardRag.IsRed(x.Period.PeriodScore)))
            .OrderBy(x => x.Period!.PeriodScore ?? decimal.MaxValue)
            .Take(ExceptionKpiCount)
            .Select(x => KpiRow(load, x.Kpi, x.Period!, "Exception"))
            .ToList();

        return new TableWidgetDto
        {
            Title = "KPI exceptions",
            Description = "KPIs that are off track or still without data for the selected period.",
            Columns = new[]
            {
                Col("kpi", "KPI"),
                Col("kra", "KRA"),
                Col("target", "Target", "number", "right"),
                Col("actual", "Actual", "number", "right"),
                Col("score", "Score", "percent", "right"),
                Col("rag", "RAG", "rag")
            },
            Rows = rows,
            EmptyMessage = "No KPI exceptions for the selected period."
        };
    }

    private async Task<TableWidgetDto> BuildKpiExceptionsAsync(DashboardLoad load, CancellationToken cancellationToken)
        => await Task.FromResult(BuildKpiExceptions(load));

    private TableWidgetDto BuildStrategicAlignment(DashboardLoad load)
    {
        var rows = new List<TableRowDto>();

        foreach (var goal in load.Goals)
        {
            var goalKpis = load.Kpis.Where(k => k.Kra?.GoalId == goal.Id).ToList();

            foreach (var group in goalKpis.GroupBy(k => k.Kra!.Id))
            {
                var kra = group.First().Kra!;
                var kraScore = KraScore(load, kra.Id);

                foreach (var kpi in group.OrderBy(k => k.Name))
                {
                    var period = KpiPeriod(load, kpi.Id);

                    rows.Add(Row(
                        kpi.Id.ToString(),
                        null,
                        ("goal", goal.Name),
                        ("goalScore", Percent(GoalScore(load, goal.Id))),
                        ("kra", kra.Name),
                        ("kraScore", Percent(kraScore)),
                        ("kpi", kpi.Name),
                        ("weight", kpi.Weight.ToString("0.##", CultureInfo.InvariantCulture)),
                        ("kpiType", kpi.KpiType == KpiType.Lagging ? "Lagging" : "Leading"),
                        ("score", Percent(period?.PeriodScore)),
                        ("rag", DashboardRag.FromScore(period?.PeriodScore))
                    ));
                }
            }
        }

        return new TableWidgetDto
        {
            Title = "Strategic alignment",
            Description = "Goal to KRA to KPI relationships and how each layer is contributing.",
            Columns = new[]
            {
                Col("goal", "Goal"),
                Col("goalScore", "Goal score", "percent", "right"),
                Col("kra", "KRA"),
                Col("kraScore", "KRA score", "percent", "right"),
                Col("kpi", "KPI"),
                Col("kpiType", "Type"),
                Col("weight", "Weight %", "number", "right"),
                Col("score", "KPI score", "percent", "right"),
                Col("rag", "RAG", "rag")
            },
            Rows = rows,
            EmptyMessage = "No goal to KRA to KPI alignment is available for this scorecard."
        };
    }

    private async Task<TableWidgetDto> BuildStrategicAlignmentAsync(DashboardLoad load, CancellationToken cancellationToken)
        => await Task.FromResult(BuildStrategicAlignment(load));

    // ------------------------------------------------------------------ initiatives

    private TableWidgetDto BuildInitiativeStatus(DashboardLoad load)
    {
        var rows = load.Initiatives
            .OrderBy(i => i.Status)
            .ThenByDescending(i => i.Priority)
            .Select(i => Row(
                i.Id.ToString(),
                $"/initiatives/{i.Id}",
                ("initiative", i.Name),
                ("status", InitiativeStatusText(i.Status)),
                ("statusTone", InitiativeStatusTone(i.Status)),
                ("priority", i.Priority.ToString()),
                ("progress", InitiativeProgress(i)),
                ("milestones", i.Milestones?.Count ?? 0),
                ("openMilestones", i.Milestones?.Count(m => m.CompletionPercentage < 100) ?? 0),
                ("start", Date(i.StartDate)),
                ("end", Date(i.EndDate)),
                ("budget", Money(i.Budget))))
            .ToList();

        return new TableWidgetDto
        {
            Title = "Initiative status",
            Description = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                ? "Initiatives you own."
                : "Not started, in progress, completed and on hold initiatives.",
            Columns = new[]
            {
                Col("initiative", "Initiative"),
                Col("status", "Status"),
                Col("priority", "Priority"),
                Col("progress", "Progress %", "number", "right"),
                Col("milestones", "Milestones", "number", "right"),
                Col("openMilestones", "Open", "number", "right"),
                Col("start", "Start", "date"),
                Col("end", "End", "date"),
                Col("budget", "Budget", "money", "right")
            },
            Rows = rows,
            EmptyMessage = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                ? "No initiatives are assigned to you."
                : "No initiatives have been created yet.",
            ShowRowLinks = true
        };
    }

    private async Task<TableWidgetDto> BuildInitiativeStatusAsync(DashboardLoad load, CancellationToken cancellationToken)
    {
        if (load.Initiatives.Count == 0 && load.Profile.DataScope != DashboardDataScope.AssignedOnly)
        {
            load.Initiatives = await initiativeRepository.GetAllWithMilestonesAsync();
        }

        return BuildInitiativeStatus(load);
    }

    private CompositeWidgetDto BuildBudgetSummary(DashboardLoad load)
    {
        var total = load.Initiatives.Sum(i => i.Budget);
        var completed = load.Initiatives.Where(i => i.Status == InitiativeStatus.Completed).Sum(i => i.Budget);
        var active = load.Initiatives.Where(i => i.Status == InitiativeStatus.InProgress).Sum(i => i.Budget);
        var notStarted = load.Initiatives.Where(i => i.Status == InitiativeStatus.NotStarted).Sum(i => i.Budget);

        var rows = load.Initiatives
            .GroupBy(i => i.Status)
            .Select(g =>
            {
                var budget = g.Sum(i => i.Budget);
                var progress = Math.Round((decimal)g.Average(i => InitiativeProgress(i)), 1);
                var variance = budget <= 0m ? 0m : Math.Round(progress - 100m, 1);
                var share = total <= 0m ? 0m : Math.Round(budget / total * 100m, 1);

                return new
                {
                    Status = g.Key,
                    Budget = budget,
                    Row = Row(
                        null,
                        null,
                        ("status", (object?)InitiativeStatusText(g.Key)),
                        ("initiatives", (object?)g.Count()),
                        ("budget", (object?)budget),
                        ("share", (object?)share),
                        ("progress", (object?)progress),
                        ("variance", (object?)variance))
                };
            })
            .OrderByDescending(x => x.Budget)
            .Select(x => x.Row)
            .ToList();

        return new CompositeWidgetDto
        {
            Title = "Budget summary",
            Description = "Planned budget by status. Actual spend is not tracked in the current schema, so variance is shown as delivery progress against plan.",
            Cards = new[]
            {
                new StatCardDto { Key = "total", Label = "Total planned budget", Value = Money(total), Tone = "primary" },
                new StatCardDto { Key = "active", Label = "In progress", Value = Money(active) },
                new StatCardDto { Key = "completed", Label = "Completed", Value = Money(completed) },
                new StatCardDto { Key = "planned", Label = "Not started", Value = Money(notStarted) }
            },
            Columns = new[]
            {
                Col("status", "Status"),
                Col("initiatives", "Initiatives", "number", "right"),
                Col("budget", "Planned budget", "money", "right"),
                Col("share", "Share %", "percent", "right"),
                Col("progress", "Delivery %", "percent", "right"),
                Col("variance", "Variance", "number", "right")
            },
            Rows = rows,
            EmptyMessage = "No initiative budgets are available."
        };
    }

    // ------------------------------------------------------------------ divisions

    /// <summary>
    /// Calculates every division plan for the selected period once. The table and the
    /// comparison chart both read this, because the calculation is one query per plan.
    /// </summary>
    private async Task<IReadOnlyList<DivisionScoreEntry>> DivisionScoresAsync(
        DashboardLoad load,
        CancellationToken cancellationToken)
    {
        var scorecardId = load.SelectedScorecard?.Id;
        var fiscalYearId = load.FiscalYear?.Id;

        if (scorecardId is null || fiscalYearId is null)
        {
            return Array.Empty<DivisionScoreEntry>();
        }

        var selectedScorecardId = scorecardId.Value;
        var selectedFiscalYearId = fiscalYearId.Value;

        var plans = await divisionPlanRepository.GetByScorecardAsync(selectedScorecardId);
        var scope = load.SelectedPeriodResult?.Scope;
        var entries = new List<DivisionScoreEntry>();

        foreach (var plan in plans)
        {
            var divisionResult = await divisionPeriodCalculationService.CalculateAsync(
                plan.Id,
                selectedScorecardId,
                selectedFiscalYearId,
                scope is null ? PeriodType.Ytd : MapGranularity(scope.Granularity),
                scope?.Quarter,
                scope?.FromMonth,
                scope?.ToMonth,
                null,
                cancellationToken);

            entries.Add(new DivisionScoreEntry(
                plan.Division?.Name ?? plan.DivisionId.ToString(),
                divisionResult.OverallScore,
                plan.PlanStatus.ToString(),
                divisionResult.DivisionGoals.Count,
                divisionResult.Kpis.Count,
                divisionResult.Kpis.Values.Count(k => k.PeriodScore.HasValue),
                $"/division-planning/{selectedScorecardId}?divisionId={plan.DivisionId}"));
        }

        return entries;
    }

    private static TableWidgetDto BuildDivisionComparison(DashboardLoad load, IReadOnlyList<DivisionScoreEntry> divisions)
    {
        return new TableWidgetDto
        {
            Title = "Division comparison",
            Description = "Division plan scores for the selected period.",
            Columns = new[]
            {
                Col("division", "Division"),
                Col("planStatus", "Plan status"),
                Col("divisionGoals", "Goals", "number", "right"),
                Col("kpis", "KPIs", "number", "right"),
                Col("kpisWithData", "With data", "number", "right"),
                Col("score", "Score", "percent", "right"),
                Col("rag", "RAG", "rag")
            },
            Rows = divisions
                .OrderByDescending(d => d.Score ?? decimal.MinValue)
                .ThenBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => Row(
                    null,
                    d.Route,
                    ("division", (object?)d.Name),
                    ("planStatus", (object?)d.PlanStatus),
                    ("divisionGoals", (object?)d.DivisionGoals),
                    ("kpis", (object?)d.Kpis),
                    ("kpisWithData", (object?)d.KpisWithData),
                    ("score", (object?)Percent(d.Score)),
                    ("rag", DashboardRag.FromScore(d.Score))))
                .ToList(),
            EmptyMessage = "No division plans exist for this scorecard."
        };
    }

    private static PeriodType MapGranularity(SnapshotGranularity granularity) => granularity switch
    {
        SnapshotGranularity.Quarter => PeriodType.Quarter,
        SnapshotGranularity.Month => PeriodType.Month,
        SnapshotGranularity.Year => PeriodType.Year,
        _ => PeriodType.Ytd
    };

    // ------------------------------------------------------------------ data entry

    private TableWidgetDto BuildAssignedKpis(DashboardLoad load)
    {
        var scope = load.SelectedPeriodResult?.Scope;
        var lastMonth = scope?.ToMonth ?? load.CurrentFiscalMonth;
        var lockState = lastMonth > 0 && load.LocksByMonth.TryGetValue(lastMonth, out var l) ? l.State : PeriodLockState.Locked;
        var rows = load.Kpis
            .OrderBy(k => k.Kra?.GoalId.ToString(), StringComparer.Ordinal)
            .ThenBy(k => k.Kra?.Name, StringComparer.Ordinal)
            .ThenBy(k => k.Name, StringComparer.Ordinal)
            .Select(kpi =>
            {
                var period = KpiPeriod(load, kpi.Id);
                var hasData = period?.PeriodActual.HasValue == true;
                var note = lastMonth > 0 ? NoteFor(load, kpi.Id, lastMonth) : null;

                var entryStatus = lockState == PeriodLockState.Locked
                    ? "Locked"
                    : hasData
                        ? "Entered"
                        : "Missing";

                return Row(
                    kpi.Id.ToString(),
                    "/kpis",
                    ("kpi", kpi.Name),
                    ("goal", GoalName(load, kpi.Kra?.GoalId)),
                    ("kra", kpi.Kra?.Name ?? "-"),
                    ("unit", kpi.Unit),
                    ("optimization", OptimisationText(kpi.OptimizationType)),
                    ("target", period?.PeriodTarget),
                    ("actual", period?.PeriodActual),
                    ("score", Percent(period?.PeriodScore)),
                    ("rag", DashboardRag.FromScore(period?.PeriodScore)),
                    ("entryStatus", entryStatus),
                    ("periodState", LockStateText(lockState)),
                    ("note", note ?? "-")
                );
            })
            .ToList();

        return new TableWidgetDto
        {
            Title = "My KPIs",
            Description = "Actual against target for the KPIs assigned to you, with data entry status and evidence.",
            Columns = new[]
            {
                Col("kpi", "KPI"),
                Col("goal", "Goal"),
                Col("kra", "KRA"),
                Col("unit", "Unit"),
                Col("target", "Target", "number", "right"),
                Col("actual", "Actual", "number", "right"),
                Col("score", "Score", "percent", "right"),
                Col("rag", "RAG", "rag"),
                Col("entryStatus", "Entry status"),
                Col("periodState", "Period"),
                Col("note", "Comment / evidence")
            },
            Rows = rows,
            EmptyMessage = "No KPIs are assigned to you for this scorecard."
        };
    }

    private async Task<TableWidgetDto> BuildAssignedKpisAsync(DashboardLoad load, CancellationToken cancellationToken)
        => await Task.FromResult(BuildAssignedKpis(load));

    private CompositeWidgetDto BuildDataEntryStatus(DashboardLoad load)
    {
        var scope = load.SelectedPeriodResult?.Scope;
        var toMonth = scope?.ToMonth ?? load.CurrentFiscalMonth;
        var yearResult = load.SelectedYearResult;
        var months = load.Periods.Where(p => p.FiscalMonth <= toMonth).OrderBy(p => p.FiscalMonth).ToList();

        var rows = new List<TableRowDto>();
        var enteredTotal = 0;
        var missingTotal = 0;
        var currentEntered = 0;
        var currentMissing = 0;

        foreach (var period in months)
        {
            var entered = load.Kpis.Count(k =>
                yearResult is not null
                && yearResult.Kpis.TryGetValue(k.Id, out var kpiPeriod)
                && kpiPeriod.MonthlyActuals.TryGetValue(period.FiscalMonth, out var value)
                && value.HasValue);

            var missing = load.Kpis.Count - entered;
            enteredTotal += entered;
            missingTotal += missing;

            if (period.FiscalMonth == toMonth)
            {
                currentEntered = entered;
                currentMissing = missing;
            }

            var lockInfo = load.LocksByMonth.GetValueOrDefault(period.FiscalMonth);
            var cutoff = lockInfo?.EffectiveCutoffDate;
            var overdue = lockInfo is not null
                && lockInfo.State == PeriodLockState.Open
                && cutoff.HasValue
                && cutoff.Value.Date <= load.NowUtc.Date
                && missing > 0;

            rows.Add(Row(
                null,
                null,
                ("month", MonthLabel(period)),
                ("period", $"M{period.FiscalMonth}"),
                ("quarter", period.Quarter),
                ("lockState", LockStateText(lockInfo?.State)),
                ("assigned", load.Kpis.Count),
                ("entered", entered),
                ("missing", missing),
                ("completion", load.Kpis.Count == 0 ? 0 : Math.Round(entered * 100m / load.Kpis.Count, 1)),
                ("cutoff", cutoff.HasValue ? Date(cutoff) : "-"),
                ("overdue", overdue ? "Overdue" : "On track")
            ));
        }

        var openPeriods = load.LocksByMonth.Values.Count(l => l.State == PeriodLockState.Open);
        var lockedPeriods = load.LocksByMonth.Values.Count(l => l.State == PeriodLockState.Locked);

        return new CompositeWidgetDto
        {
            Title = "Data entry status",
            Description = "How much of each period has been entered for the KPIs in scope.",
            Cards = new[]
            {
                new StatCardDto
                {
                    Key = "currentPeriod",
                    Label = "Current period",
                    Value = toMonth > 0 ? MonthLabel(load.FiscalYear, toMonth) : "-",
                    Detail = $"M{toMonth}"
                },
                new StatCardDto { Key = "entered", Label = "Entered this period", Value = currentEntered.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto
                {
                    Key = "missing",
                    Label = "Missing this period",
                    Value = currentMissing.ToString(CultureInfo.InvariantCulture),
                    Tone = currentMissing > 0 ? "warning" : "success"
                },
                new StatCardDto { Key = "open", Label = "Open periods", Value = openPeriods.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto { Key = "locked", Label = "Locked periods", Value = lockedPeriods.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto
                {
                    Key = "ytdMissing",
                    Label = "Missing entries YTD",
                    Value = missingTotal.ToString(CultureInfo.InvariantCulture),
                    Detail = $"{enteredTotal} entered"
                }
            },
            Columns = new[]
            {
                Col("month", "Month"),
                Col("state", "Period state"),
                Col("assigned", "KPIs in scope", "number", "right"),
                Col("entered", "Entered", "number", "right"),
                Col("missing", "Missing", "number", "right"),
                Col("completion", "Completion %", "percent", "right"),
                Col("cutoff", "Cutoff date", "date"),
                Col("overdue", "Data entry")
            },
            Rows = rows,
            EmptyMessage = "No fiscal periods are configured for this fiscal year."
        };
    }

    private static string LockStateText(PeriodLockState? state) => state switch
    {
        PeriodLockState.Open => "Open",
        PeriodLockState.Locked => "Locked",
        PeriodLockState.NotYetOpen => "Not yet open",
        _ => "-"
    };

    private async Task<CompositeWidgetDto> BuildDataEntryStatusAsync(DashboardLoad load, CancellationToken cancellationToken)
        => await Task.FromResult(BuildDataEntryStatus(load));
    private TableWidgetDto BuildPeriodLocks(DashboardLoad load)
    {
        var rows = load.LocksByMonth
            .OrderBy(x => x.Key)
            .Select(x =>
            {
                var period = load.Periods.FirstOrDefault(p => p.FiscalMonth == x.Key);
                var lockInfo = x.Value;

                return Row(
                    null,
                    null,
                    ("month", period is null ? $"M{x.Key}" : MonthLabel(period)),
                    ("period", $"M{x.Key}"),
                    ("quarter", period?.Quarter.ToString() ?? "-"),
                    ("lockState", LockStateText(lockInfo.State)),
                    ("entryOpen", Date(lockInfo.EffectiveEntryOpenDate)),
                    ("cutoff", Date(lockInfo.EffectiveCutoffDate)),
                    ("reminderDays", lockInfo.EffectiveReminderDaysBeforeCutoff),
                    ("override", lockInfo.HasExplicitOverride ? "Manual override" : "System default"),
                    ("lockedAt", lockInfo.LockedAtUtc.HasValue ? Date(lockInfo.LockedAtUtc) : "-")
                );
            })
            .ToList();

        return new TableWidgetDto
        {
            Title = "Period locking",
            Description = "Entry windows, cutoffs and lock state for every fiscal month.",
            Columns = new[]
            {
                Col("month", "Month"),
                Col("quarter", "Quarter", "number", "right"),
                Col("lockState", "State"),
                Col("entryOpen", "Entry opens", "date"),
                Col("cutoff", "Cutoff", "date"),
                Col("reminderDays", "Reminder (days)", "number", "right"),
                Col("override", "Source"),
                Col("lockedAt", "Locked at", "date")
            },
            Rows = rows,
            EmptyMessage = "No fiscal periods are configured for this fiscal year."
        };
    }

    // ------------------------------------------------------------------ administration

    private async Task<CompositeWidgetDto> BuildSystemStatsAsync(CancellationToken cancellationToken)
    {
        var users = await userRepository.GetAll();
        var roles = await roleRepository.GetAll();
        var scorecards = await scorecardRepository.GetAll();
        var goals = await goalRepository.GetAll();
        var kpis = await kpiRepository.GetAll();
        var initiatives = await initiativeRepository.GetAll();
        var activeUsers = users.Count(u => u.IsActive);

        return new CompositeWidgetDto
        {
            Title = "System statistics",
            Description = "Users, roles and planning object counts held by the system.",
            Cards = new[]
            {
                new StatCardDto { Key = "users", Label = "Users", Value = users.Count.ToString(CultureInfo.InvariantCulture), Detail = $"{activeUsers} active", Route = "/admin/users" },
                new StatCardDto { Key = "roles", Label = "Roles", Value = roles.Count.ToString(CultureInfo.InvariantCulture), Route = "/admin/roles" },
                new StatCardDto { Key = "scorecards", Label = "Scorecards", Value = scorecards.Count.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto { Key = "goals", Label = "Goals", Value = goals.Count.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto { Key = "kpis", Label = "KPIs", Value = kpis.Count.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto { Key = "initiatives", Label = "Initiatives", Value = initiatives.Count.ToString(CultureInfo.InvariantCulture) },
                new StatCardDto
                {
                    Key = "inactive",
                    Label = "Inactive users",
                    Value = (users.Count - activeUsers).ToString(CultureInfo.InvariantCulture),
                    Tone = users.Count - activeUsers > 0 ? "warning" : "success"
                }
            }
        };
    }

    private async Task<TableWidgetDto> BuildAuditActivityAsync(CancellationToken cancellationToken)
    {
        var entries = await auditLogRepository.GetRecentAsync(MaxAuditRows);

        var rows = entries
            .Select(e => Row(
                e.Id.ToString(),
                null,
                ("performedAt", e.PerformedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)),
                ("user", string.IsNullOrWhiteSpace(e.PerformedByName) ? "System" : e.PerformedByName),
                ("action", e.Action.ToString()),
                ("description", AuditDescription.For(e.EntityType, e.Action)),
                ("entity", e.EntityType),
                ("entityId", e.EntityId.ToString())))
            .ToList();

        return new TableWidgetDto
        {
            Title = "Recent audit activity",
            Description = "Security relevant changes recorded by the audit interceptor.",
            Columns = new[]
            {
                Col("performedAt", "When", "date"),
                Col("user", "User"),
                Col("action", "Action"),
                Col("description", "Change")
            },
            Rows = rows,
            EmptyMessage = "No audit activity has been recorded yet."
        };
    }

    private static TableWidgetDto BuildReportsSummary(DashboardLoad load)
    {
        var reports = new[]
        {
            new { name = "Scorecard performance", description = "Performance by scorecard, goal, KRA or KPI for a period.", level = "Scorecard / Goal / KRA / KPI" },
            new { name = "KPI detail", description = "Monthly target, actual, score and RAG per KPI.", level = "KPI" },
            new { name = "Division performance", description = "Division KPI targets, actuals and scores by period.", level = "Division" },
            new { name = "Initiative status", description = "Initiative progress, priority, owner and budget.", level = "Initiative" }
        };

        return new TableWidgetDto
        {
            Title = "Performance summaries",
            Description = "Read-only report views available to this role.",
            Columns = new[]
            {
                Col("name", "Report"),
                Col("level", "Available at"),
                Col("description", "Description")
            },
            Rows = reports
                .Select(r => Row(null, "/reports", ("name", r.name), ("level", r.level), ("description", r.description)))
                .ToList(),
            ShowRowLinks = true
        };
    }

    // ------------------------------------------------------------------ alerts

    private async Task<AlertListWidgetDto> BuildAlertsAsync(DashboardLoad load, CancellationToken cancellationToken)
    {
        var alerts = new List<AlertDto>();

        // Red KPIs for more than two consecutive months.
        var year = load.SelectedYearResult;
        if (year is not null)
        {
            foreach (var kpi in load.Kpis)
            {
                if (!year.Kpis.TryGetValue(kpi.Id, out var period))
                {
                    continue;
                }

                var streak = 0;
                var months = load.Periods
                    .Where(p => load.CurrentFiscalMonth <= 0 || p.FiscalMonth <= load.CurrentFiscalMonth)
                    .Select(p => p.FiscalMonth)
                    .OrderByDescending(m => m)
                    .ToList();

                foreach (var month in months)
                {
                    if (period.MonthlyScores.TryGetValue(month, out var score) && DashboardRag.IsRed(score))
                    {
                        streak++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (streak >= ConsecutiveRedMonthsAlertThreshold)
                {
                    alerts.Add(new AlertDto
                    {
                        Key = $"kpi-red-{kpi.Id}",
                        Severity = "danger",
                        Title = $"{kpi.Name} off track for {streak} consecutive months",
                        Description = $"Monthly score stayed below {DashboardRag.AmberThreshold:F0}% for {streak} months in a row.",
                        Route = "/kpis"
                    });
                }
            }
        }

        // Overdue initiatives and milestones approaching their deadline.
        foreach (var initiative in load.Initiatives)
        {
            var open = initiative.Status is InitiativeStatus.NotStarted or InitiativeStatus.InProgress;

            if (open && initiative.EndDate.Date < load.NowUtc.Date)
            {
                alerts.Add(new AlertDto
                {
                    Key = $"initiative-overdue-{initiative.Id}",
                    Severity = "danger",
                    Title = $"{initiative.Name} passed its end date",
                    Description = $"End date {Date(initiative.EndDate)} has passed while the initiative is still {InitiativeStatusText(initiative.Status).ToLowerInvariant()}.",
                    Route = $"/initiatives/{initiative.Id}"
                });
            }

            foreach (var milestone in initiative.Milestones ?? new List<Milestone>())
            {
                if (milestone.CompletionPercentage >= 100)
                {
                    continue;
                }

                var dueDate = milestone.DueDate;
                var days = (dueDate.Date - load.NowUtc.Date).Days;
                if (days < 0)
                {
                    alerts.Add(new AlertDto
                    {
                        Key = $"milestone-overdue-{milestone.Id}",
                        Severity = "danger",
                        Title = $"Milestone \"{milestone.Name}\" is overdue",
                        Description = $"Due {Date(dueDate)} on initiative {initiative.Name}.",
                        Route = $"/initiatives/{initiative.Id}"
                    });
                }
                else if (days <= MilestoneDeadlineWindowDays)
                {
                    alerts.Add(new AlertDto
                    {
                        Key = $"milestone-due-{milestone.Id}",
                        Severity = "warning",
                        Title = $"Milestone \"{milestone.Name}\" is due in {days} day{(days == 1 ? string.Empty : "s")}",
                        Description = $"Due {Date(dueDate)} on initiative {initiative.Name}, currently {milestone.CompletionPercentage}% complete.",
                        Route = $"/initiatives/{initiative.Id}"
                    });
                }
            }
        }

        // Missing actuals for the current period while the entry window is still open.
        var currentMonth = load.CurrentFiscalMonth;
        if (currentMonth > 0 && load.Kpis.Count > 0)
        {
            var missing = load.Kpis
                .Where(k => load.Actuals.All(a => a.KpiId != k.Id || a.FiscalPeriod?.FiscalMonth != currentMonth))
                .ToList();

            if (missing.Count > 0)
            {
                var scope = load.Profile.DataScope == DashboardDataScope.AssignedOnly
                    ? "your assigned KPIs"
                    : "the KPIs in scope";

                alerts.Add(new AlertDto
                {
                    Key = "missing-actuals",
                    Severity = "warning",
                    Title = $"{missing.Count} KPI{(missing.Count == 1 ? string.Empty : "s")} without actuals for {MonthLabel(load.FiscalYear, currentMonth)}",
                    Description = $"No actual value has been recorded for {scope} in the current period.",
                    Route = "/data-entry"
                });
            }
        }

        // Data entry overdue: an open period past its cutoff with gaps still present.
        foreach (var (period, missingCount) in DataEntryGaps(load))
        {
            alerts.Add(new AlertDto
            {
                Key = $"overdue-entry-{period.FiscalMonth}",
                Severity = "danger",
                Title = $"Data entry overdue for {MonthLabel(period)}",
                Description = $"{missingCount} KPI{(missingCount == 1 ? string.Empty : "s")} still missing after the cutoff date.",
                Route = "/data-entry"
            });
        }

        // Configuration alerts for administrators.
        if (load.Granted(Permissions.AdminConfigManage) && load.SelectedScorecard is not null)
        {
            var kpis = await kpiRepository.GetByScorecardAsync(load.SelectedScorecard.Id);
            if (kpis.Count > 0)
            {
                var missingTargets = kpis.Count(k => load.Targets.All(t => t.KpiId != k.Id));
                if (missingTargets > 0)
                {
                    alerts.Add(new AlertDto
                    {
                        Key = "missing-targets",
                        Severity = "warning",
                        Title = $"{missingTargets} KPI{(missingTargets == 1 ? string.Empty : "s")} without configured targets",
                        Description = "Targets are required before KPI scores can be calculated.",
                        Route = "/target-config"
                    });
                }
            }

            if (load.FiscalYear is not null && load.FiscalYear.FiscalYearStatus != FiscalYearStatus.Open)
            {
                alerts.Add(new AlertDto
                {
                    Key = "closed-fiscal-year",
                    Severity = "info",
                    Title = $"Fiscal year {load.FiscalYear.Label} is {load.FiscalYear.FiscalYearStatus}",
                    Description = "Scores are read-only while the fiscal year is not open.",
                    Route = "/admin/fiscal-years"
                });
            }
        }

        var ordered = alerts
            .OrderBy(a => a.Severity switch { "danger" => 0, "warning" => 1, _ => 2 })
            .ThenByDescending(a => a.OccurredAt)
            .ToList();

        return new AlertListWidgetDto
        {
            Title = "Alerts requiring attention",
            Description = "Rule based alerts limited to the data this role is permitted to see.",
            Alerts = ordered,
            EmptyMessage = "Nothing needs attention right now."
        };
    }

    private IEnumerable<(FiscalPeriod Period, int Missing)> DataEntryGaps(DashboardLoad load)
    {
        var year = load.SelectedYearResult;
        if (year is null)
        {
            yield break;
        }

        var scope = load.SelectedPeriodResult?.Scope;
        var toMonth = scope?.ToMonth ?? load.CurrentFiscalMonth;

        foreach (var period in load.Periods.Where(p => p.FiscalMonth <= toMonth).OrderBy(p => p.FiscalMonth))
        {
            var lockInfo = load.LocksByMonth.GetValueOrDefault(period.FiscalMonth);
            if (lockInfo is null || lockInfo.State != PeriodLockState.Open)
            {
                continue;
            }

            if (lockInfo.EffectiveCutoffDate != default && lockInfo.EffectiveCutoffDate.Date > load.NowUtc.Date)
            {
                continue;
            }

            var missing = load.Kpis.Count(k =>
                !year.Kpis.TryGetValue(k.Id, out var kpiPeriod)
                || !kpiPeriod.MonthlyActuals.TryGetValue(period.FiscalMonth, out var value)
                || !value.HasValue);

            if (missing > 0)
            {
                yield return (period, missing);
            }
        }
    }
}
