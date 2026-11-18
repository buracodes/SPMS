using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;

namespace Application.Tests.Calculation;

/// <summary>
/// FR-602/603/604 roll-up driven through the real period calculation service, so the whole
/// KPI -> KRA -> Goal -> Scorecard chain is exercised. A valid score of 0 contributes as 0;
/// a missing (null) score is excluded and the remaining weights are renormalized.
/// </summary>
public class ScorecardRollupScenarioTests
{
    private sealed record GoalSpec(
        int Weight,
        decimal? MonthlyTarget,
        decimal? MonthlyActual,
        KpiType Type = KpiType.Lagging,
        bool IsCumulative = false,
        bool IsActive = true);

    private static async Task<(FiscalYear Year, Scorecard Scorecard, IReadOnlyList<Guid> Goals, IReadOnlyList<Guid> Kpis)>
        SeedAsync(TestDbContextLease lease, params GoalSpec[] specs)
    {
        var context = lease.Context;
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);
        var periods = calendar.GeneratePeriods(year);
        context.FiscalYears.Add(year);
        context.FiscalPeriods.AddRange(periods);
        await context.SaveChangesAsync();

        var firstMonth = periods.Single(p => p.FiscalMonth == 1);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Corporate scorecard",
            Scope = ScorecardScope.Corporate,
            ScorecardStatus = ScorecardStatus.Active
        };
        context.Scorecards.Add(scorecard);

        var goalIds = new List<Guid>();
        var kpiIds = new List<Guid>();

        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            var goal = new Goal
            {
                Id = Guid.NewGuid(),
                ScorecardId = scorecard.Id,
                Name = $"Goal {i + 1}",
                Weight = spec.Weight,
                IsActive = spec.IsActive
            };
            var kra = new Kra
            {
                Id = Guid.NewGuid(),
                GoalId = goal.Id,
                ScorecardId = scorecard.Id,
                Name = $"KRA {i + 1}",
                Weight = 100
            };
            var kpi = new Kpi
            {
                Id = Guid.NewGuid(),
                ScorecardId = scorecard.Id,
                KraId = kra.Id,
                Name = $"KPI {i + 1}",
                Weight = 100,
                Unit = "units",
                IsActive = true,
                IsCumulative = spec.IsCumulative,
                OptimizationType = KpiOptimizationType.Maximize,
                KpiType = spec.Type
            };

            goalIds.Add(goal.Id);
            kpiIds.Add(kpi.Id);
            context.AddRange(goal, kra, kpi);

            if (spec.MonthlyTarget.HasValue)
            {
                context.KpiTargets.Add(new KpiTarget
                {
                    KpiId = kpi.Id,
                    FiscalPeriodId = firstMonth.Id,
                    Granularity = TargetGranularity.Monthly,
                    TargetValue = spec.MonthlyTarget.Value
                });
            }

            if (spec.MonthlyActual.HasValue)
            {
                context.KpiActualValues.Add(new KpiActualValue
                {
                    KpiId = kpi.Id,
                    FiscalPeriodId = firstMonth.Id,
                    Value = spec.MonthlyActual.Value,
                    DataSource = KpiDataSource.Manual,
                    AsOfDate = firstMonth.EndDate
                });
            }
        }

        await context.SaveChangesAsync();
        return (year, scorecard, goalIds, kpiIds);
    }

    private static Task<ScorePeriodResult> CalculateMonthOneAsync(
        TestDbContextLease lease, Scorecard scorecard, FiscalYear year)
    {
        var context = lease.Context;
        var service = new ScorePeriodCalculationService(
            new ScorecardRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new GoalRepository(context),
            new KpiRepository(context),
            new KpiTargetRepository(context),
            new KpiActualValueRepository(context),
            new KpiScoreCalculator(new ScoreStrategyFactory()),
            new KraScoreCalculator(),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            TestBusinessClock.Real());

        return service.CalculateAsync(scorecard.Id, year.Id, PeriodType.Month, null, 1, null, null);
    }

    [Fact]
    public async Task Scenario_A_valid_zero_and_full_score_roll_up_to_fifty()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, kpis) = await SeedAsync(
            lease,
            new GoalSpec(50, 100, 0),
            new GoalSpec(50, 100, 100));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        // The zero is a real score, not missing data: it stays 0 in every level.
        Assert.Equal(0m, result.Kpis[kpis[0]].PeriodScore);
        Assert.Equal(0m, result.Kpis[kpis[0]].MonthlyScores[1]);
        Assert.Equal(0m, result.Goals[goals[0]].PeriodScore);
        Assert.Equal(100m, result.Goals[goals[1]].PeriodScore);
        Assert.Equal(50m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Scenario_B_goal_without_a_valid_score_is_excluded_not_treated_as_zero()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, kpis) = await SeedAsync(
            lease,
            new GoalSpec(50, 100, null),
            new GoalSpec(50, 100, 100));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Null(result.Kpis[kpis[0]].PeriodScore);
        Assert.Null(result.Goals[goals[0]].PeriodScore);
        Assert.Equal(100m, result.Goals[goals[1]].PeriodScore);
        // Only Goal 2 contributes, so its weight is the whole denominator.
        Assert.Equal(100m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Scenario_C_two_valid_zeros_are_not_null()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, _) = await SeedAsync(
            lease,
            new GoalSpec(50, 100, 0),
            new GoalSpec(50, 100, 0));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Equal(0m, result.Goals[goals[0]].PeriodScore);
        Assert.Equal(0m, result.Goals[goals[1]].PeriodScore);
        Assert.NotNull(result.Scorecards[scorecard.Id].PeriodScore);
        Assert.Equal(0m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Scenario_D_null_goals_are_dropped_and_the_remaining_score_is_kept()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, _) = await SeedAsync(
            lease,
            new GoalSpec(40, 100, 80),
            new GoalSpec(30, 100, null),
            new GoalSpec(30, null, null));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Equal(80m, result.Goals[goals[0]].PeriodScore);
        Assert.Null(result.Goals[goals[1]].PeriodScore);
        Assert.Null(result.Goals[goals[2]].PeriodScore);
        Assert.Equal(80m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Scenario_E_no_valid_goal_score_leaves_the_scorecard_score_null()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, kpis) = await SeedAsync(
            lease,
            new GoalSpec(50, null, null),
            new GoalSpec(50, 100, null));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Null(result.Kpis[kpis[0]].PeriodScore);
        Assert.All(goals, id => Assert.Null(result.Goals[id].PeriodScore));
        Assert.Null(result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Scenario_F_partial_validity_normalizes_among_the_contributing_weights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, _) = await SeedAsync(
            lease,
            new GoalSpec(40, 100, 80),
            new GoalSpec(30, 100, 60),
            new GoalSpec(30, 100, null));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Equal(80m, result.Goals[goals[0]].PeriodScore);
        Assert.Equal(60m, result.Goals[goals[1]].PeriodScore);
        Assert.Null(result.Goals[goals[2]].PeriodScore);
        Assert.Equal((80m * 40m + 60m * 30m) / 70m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Goal_with_only_leading_kpis_has_no_score_and_is_excluded()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, _) = await SeedAsync(
            lease,
            new GoalSpec(50, 100, 0, Type: KpiType.Leading),
            new GoalSpec(50, 100, 100));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        // FR-602: only Lagging KPIs roll up. The Leading KPI keeps its own displayed score.
        Assert.Null(result.Goals[goals[0]].PeriodScore);
        Assert.Equal(100m, result.Goals[goals[1]].PeriodScore);
        Assert.Equal(100m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Inactive_goal_is_excluded_from_the_scorecard_but_keeps_its_own_score()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, _) = await SeedAsync(
            lease,
            new GoalSpec(50, 100, 0),
            new GoalSpec(50, 100, 100, IsActive: false));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Equal(0m, result.Goals[goals[0]].PeriodScore);
        Assert.Equal(100m, result.Goals[goals[1]].PeriodScore);
        Assert.Equal(0m, result.Scorecards[scorecard.Id].PeriodScore);
    }

    [Fact]
    public async Task Cumulative_zero_actual_is_scored_instead_of_treated_as_missing()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (year, scorecard, goals, kpis) = await SeedAsync(
            lease,
            new GoalSpec(100, 100, 0, IsCumulative: true));

        var result = await CalculateMonthOneAsync(lease, scorecard, year);

        Assert.Equal(0m, result.Kpis[kpis[0]].PeriodActual);
        Assert.Equal(0m, result.Kpis[kpis[0]].PeriodScore);
        Assert.Equal(0m, result.Goals[goals[0]].PeriodScore);
        Assert.Equal(0m, result.Scorecards[scorecard.Id].PeriodScore);
    }
}
