using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Calculation;

/// <summary>
/// FR-605: a non-cumulative period score is the average of the monthly scores, not a score
/// computed once from averaged values. The two formulas diverge for Minimize, ExactHit and
/// Binary optimizations, so both period engines must average the monthly scores they already
/// compute. Cumulative KPIs keep scoring summed values against the summed/coarse target.
/// </summary>
public class PeriodScoreAggregationTests
{
    private static (ScorePeriodCalculationService Score, DivisionPeriodCalculationService Division) CreateEngines(
        TestDbContextLease lease, TestBusinessClock? clock = null)
    {
        var context = lease.Context;
        var factory = new ScoreStrategyFactory();
        clock ??= TestBusinessClock.Real();

        var score = new ScorePeriodCalculationService(
            new ScorecardRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new GoalRepository(context),
            new KpiRepository(context),
            new KpiTargetRepository(context),
            new KpiActualValueRepository(context),
            new KpiScoreCalculator(factory),
            new KraScoreCalculator(),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            clock);

        var division = new DivisionPeriodCalculationService(
            new DivisionGoalRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new KpiTargetRepository(context),
            new KpiActualValueRepository(context),
            new KpiScoreCalculator(factory),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            clock);

        return (score, division);
    }

    private static async Task<FiscalYear> SeedYearAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);
        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));
        await lease.Context.SaveChangesAsync();
        return year;
    }

    private static async Task<(Scorecard Scorecard, Kpi Kpi)> SeedCorporateKpiAsync(
        TestDbContextLease lease,
        FiscalYear year,
        KpiOptimizationType optimization,
        bool isCumulative)
    {
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Corporate Scorecard",
            Scope = ScorecardScope.Corporate,
            ScorecardStatus = ScorecardStatus.Active
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "KPI",
            Weight = 100,
            Unit = "units",
            IsActive = true,
            IsCumulative = isCumulative,
            OptimizationType = optimization,
            KpiType = KpiType.Lagging
        };

        lease.Context.AddRange(scorecard, goal, kra, kpi);
        await lease.Context.SaveChangesAsync();
        return (scorecard, kpi);
    }

    private static async Task<(DivisionPlan Plan, Kpi Kpi)> SeedDivisionKpiAsync(
        TestDbContextLease lease,
        Guid scorecardId,
        KpiOptimizationType optimization,
        bool isCumulative)
    {
        var division = new Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS", IsActive = true };
        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = division.Id,
            ScorecardId = scorecardId,
            PlanStatus = DivisionPlanStatus.Finalized
        };
        var parentGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecardId, Name = "Corporate goal", Weight = 100 };
        var goal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = parentGoal.Id,
            Name = "Operations delivery goal",
            Weight = 100
        };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            DivisionGoalId = goal.Id,
            Name = "Operations KPI",
            Weight = 100,
            Unit = "units",
            IsActive = true,
            IsCumulative = isCumulative,
            OptimizationType = optimization,
            KpiType = KpiType.Lagging
        };

        lease.Context.AddRange(division, plan, parentGoal, goal, kpi);
        await lease.Context.SaveChangesAsync();
        return (plan, kpi);
    }

    private static async Task SeedValuesAsync(
        TestDbContextLease lease,
        FiscalYear year,
        Guid kpiId,
        IReadOnlyDictionary<int, decimal> monthlyTargets,
        IReadOnlyDictionary<int, decimal> actuals,
        decimal? quarterlyTarget = null,
        decimal? annualTarget = null)
    {
        var periods = await lease.Context.FiscalPeriods.Where(p => p.FiscalYearId == year.Id).ToListAsync();

        foreach (var (month, value) in monthlyTargets)
        {
            lease.Context.KpiTargets.Add(new KpiTarget
            {
                KpiId = kpiId,
                FiscalPeriodId = periods.Single(p => p.FiscalMonth == month).Id,
                Granularity = TargetGranularity.Monthly,
                TargetValue = value
            });
        }

        if (quarterlyTarget.HasValue)
        {
            lease.Context.KpiTargets.Add(new KpiTarget
            {
                KpiId = kpiId,
                FiscalPeriodId = periods.Single(p => p.FiscalMonth == 3).Id,
                Granularity = TargetGranularity.Quarterly,
                TargetValue = quarterlyTarget.Value
            });
        }

        if (annualTarget.HasValue)
        {
            lease.Context.KpiTargets.Add(new KpiTarget
            {
                KpiId = kpiId,
                FiscalPeriodId = periods.Single(p => p.FiscalMonth == 12).Id,
                Granularity = TargetGranularity.Annual,
                TargetValue = annualTarget.Value
            });
        }

        foreach (var (month, value) in actuals)
        {
            var period = periods.Single(p => p.FiscalMonth == month);
            lease.Context.KpiActualValues.Add(new KpiActualValue
            {
                KpiId = kpiId,
                FiscalPeriodId = period.Id,
                Value = value,
                DataSource = KpiDataSource.Manual,
                AsOfDate = period.EndDate
            });
        }

        await lease.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Ytd_crossing_the_calendar_year_stops_at_the_current_fiscal_month()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        // Fiscal year starts 1 July: February 2027 is fiscal month 8, not month 12.
        var year = await SeedYearAsync(lease);
        var (scorecard, kpi) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Maximize, true);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal> { [8] = 100 },
            new Dictionary<int, decimal> { [8] = 50, [9] = 100 });

        var (score, _) = CreateEngines(
            lease, TestBusinessClock.On(new DateTime(2027, 2, 10, 0, 0, 0, DateTimeKind.Utc)));
        var result = await score.CalculateAsync(scorecard.Id, year.Id, PeriodType.Ytd, null, null, null, null);
        var period = result.Kpis[kpi.Id];

        Assert.Equal(8, result.Scope.ToMonth);
        // The later month is outside YTD, so its actual must not be summed into the period.
        Assert.Equal(50m, period.PeriodActual);
        Assert.Equal(50m, period.PeriodScore);
    }

    [Fact]
    public async Task Quarter_minimize_non_cumulative_averages_monthly_scores()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, kpi) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Minimize, false);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal> { [1] = 100, [2] = 100, [3] = 100 },
            new Dictionary<int, decimal> { [1] = 50, [2] = 200 });

        var (score, _) = CreateEngines(lease);
        var result = await score.CalculateAsync(scorecard.Id, year.Id, PeriodType.Quarter, 1, null, null, null);
        var period = result.Kpis[kpi.Id];

        // Monthly scores are 200% and 50%; scoring the averaged actual (125) would give 80%.
        Assert.Equal(125m, period.PeriodScore);
        Assert.Equal(125m, period.PeriodActual);
        Assert.Equal(100m, period.PeriodTarget);
    }

    [Fact]
    public async Task Quarter_binary_non_cumulative_averages_monthly_scores()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, kpi) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Binary, false);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal> { [1] = 1, [2] = 1, [3] = 1 },
            new Dictionary<int, decimal> { [1] = 1, [2] = 0, [3] = 1 });

        var (score, _) = CreateEngines(lease);
        var result = await score.CalculateAsync(scorecard.Id, year.Id, PeriodType.Quarter, 1, null, null, null);
        var period = result.Kpis[kpi.Id];

        // Two of three months hit; scoring the averaged actual (0.67) would give 0%.
        Assert.NotNull(period.PeriodScore);
        Assert.True(Math.Abs(period.PeriodScore!.Value - 200m / 3m) < 0.000001m);
    }

    [Fact]
    public async Task Quarter_maximize_non_cumulative_keeps_the_same_score_as_averaged_values()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, kpi) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Maximize, false);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal> { [1] = 100, [2] = 100, [3] = 100 },
            new Dictionary<int, decimal> { [1] = 50, [2] = 200 });

        var (score, _) = CreateEngines(lease);
        var result = await score.CalculateAsync(scorecard.Id, year.Id, PeriodType.Quarter, 1, null, null, null);

        Assert.Equal(125m, result.Kpis[kpi.Id].PeriodScore);
    }

    [Fact]
    public async Task Quarter_minimize_cumulative_still_scores_summed_values()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, kpi) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Minimize, true);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal> { [1] = 100, [2] = 100, [3] = 100 },
            new Dictionary<int, decimal> { [1] = 50, [2] = 200 },
            quarterlyTarget: 300);

        var (score, _) = CreateEngines(lease);
        var result = await score.CalculateAsync(scorecard.Id, year.Id, PeriodType.Quarter, 1, null, null, null);
        var period = result.Kpis[kpi.Id];

        Assert.Equal(250m, period.PeriodActual);
        Assert.Equal(300m, period.PeriodTarget);
        Assert.Equal(120m, period.PeriodScore);
    }

    [Fact]
    public async Task Year_non_cumulative_with_only_an_annual_target_falls_back_to_period_values()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, kpi) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Minimize, false);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal>(),
            new Dictionary<int, decimal> { [1] = 50, [2] = 200 },
            annualTarget: 100);

        var (score, _) = CreateEngines(lease);
        var result = await score.CalculateAsync(scorecard.Id, year.Id, PeriodType.Year, null, null, null, null);
        var period = result.Kpis[kpi.Id];

        // No monthly targets means no monthly scores; the annual target still scores the average.
        Assert.Equal(100m, period.PeriodTarget);
        Assert.Equal(125m, period.PeriodActual);
        Assert.Equal(80m, period.PeriodScore);
    }

    [Fact]
    public async Task Division_quarter_minimize_non_cumulative_averages_monthly_scores()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, _) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Maximize, true);
        var (plan, kpi) = await SeedDivisionKpiAsync(lease, scorecard.Id, KpiOptimizationType.Minimize, false);
        await SeedValuesAsync(
            lease, year, kpi.Id,
            new Dictionary<int, decimal> { [1] = 100, [2] = 100, [3] = 100 },
            new Dictionary<int, decimal> { [1] = 50, [2] = 200 });

        var (_, division) = CreateEngines(lease);
        var result = await division.CalculateAsync(plan.Id, scorecard.Id, year.Id, PeriodType.Quarter, 1, null, null, null);

        Assert.Equal(125m, result.Kpis[kpi.Id].PeriodScore);
    }

    [Fact]
    public async Task Division_leading_kpis_stay_informational_and_do_not_change_scores()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var (scorecard, _) = await SeedCorporateKpiAsync(lease, year, KpiOptimizationType.Maximize, true);
        var (plan, lagging) = await SeedDivisionKpiAsync(lease, scorecard.Id, KpiOptimizationType.Maximize, true);
        lagging.Weight = 50;

        var leading = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = lagging.DivisionGoalId,
            Name = "Operations leading KPI",
            Weight = 50,
            Unit = "units",
            IsActive = true,
            IsCumulative = true,
            OptimizationType = KpiOptimizationType.Maximize,
            KpiType = KpiType.Leading
        };
        lease.Context.Add(leading);

        await SeedValuesAsync(
            lease, year, lagging.Id,
            new Dictionary<int, decimal> { [1] = 100 },
            new Dictionary<int, decimal> { [1] = 100 },
            quarterlyTarget: 100);
        await SeedValuesAsync(
            lease, year, leading.Id,
            new Dictionary<int, decimal> { [1] = 100 },
            new Dictionary<int, decimal> { [1] = 0 },
            quarterlyTarget: 100);

        var (_, division) = CreateEngines(lease);
        var result = await division.CalculateAsync(plan.Id, scorecard.Id, year.Id, PeriodType.Quarter, 1, null, null, null);

        // FR: only Lagging KPIs contribute; the Leading KPI keeps its own displayed score.
        Assert.Equal(0m, result.Kpis[leading.Id].PeriodScore);
        Assert.Equal(100m, result.DivisionGoals[lagging.DivisionGoalId!.Value].PeriodScore);
        Assert.Equal(100m, result.OverallScore);
    }
}