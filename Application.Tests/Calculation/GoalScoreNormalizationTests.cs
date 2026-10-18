using Application.Services;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Application.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests.Calculation;

public class GoalScoreNormalizationTests
{
    private readonly GoalScoreCalculator goalCalculator = new();
    private readonly ScorecardScoreCalculator scorecardCalculator = new();

    /// <summary>
    /// FR-604 proportional normalization on the task's example: assigned 40/30/20 weights
    /// with performances 80/90/70 (assigned total 90). By the approved formula the overall
    /// score is (80*40 + 90*30 + 70*20) / 90 = 7300/90 = 81.111... The example narrative
    /// states "80", which is arithmetically the plain average, not the formula result;
    /// tests assert the formula value and the discrepancy is documented in the report.
    /// </summary>
    [Fact]
    public void ComputeScorecardScoreNormalized()
    {
        var score = goalCalculator.Calculate(new List<GoalScoreContribution>
        {
            new(80m, 40m),
            new(90m, 30m),
            new(70m, 20m)
        });

        Assert.Equal(7300m / 90m, score!.Value);
    }

    /// <summary>Assigned weights summing to exactly 100 must give the same result as the raw weighted average.</summary>
    [Fact]
    public void WeightsTotalling100_MatchRawWeightedAverage()
    {
        var normalized = goalCalculator.Calculate(new List<GoalScoreContribution>
        {
            new(80m, 40m),
            new(90m, 60m)
        });

        var raw = (80m * 40m + 90m * 60m) / 100m;
        Assert.Equal(raw, normalized!.Value);
    }

    /// <summary>
    /// FR-604: contributions with no score (no data) leave numerator AND denominator;
    /// weights normalize among the goals that do have data. They are never coerced
    /// to zero, so a missing dataset cannot be silently scored as 0%.
    /// </summary>
    [Fact]
    public void ContributionsWithoutScore_AreExcludedFromNumeratorAndDenominator()
    {
        var score = goalCalculator.Calculate(new List<GoalScoreContribution>
        {
            new(80m, 40m),
            new(null, 30m),
            new(70m, 30m)
        });

        Assert.Equal(5300m / 70m, score!.Value);
    }

    [Fact]
    public void NoApplicableGoals_ReturnsNoData()
    {
        Assert.Null(goalCalculator.Calculate(new List<GoalScoreContribution>()));
        Assert.Null(goalCalculator.Calculate(new List<GoalScoreContribution> { new(null, 40m) }));
        Assert.Null(goalCalculator.Calculate(new List<GoalScoreContribution> { new(null, 0m) }));
    }

    /// <summary>
    /// Applicable weight total of zero resolves to the project's existing "no data"
    /// semantics (null) - never a divide-by-zero and never a fabricated 0% score.
    /// </summary>
    [Fact]
    public void ZeroApplicableTotalWeight_NeverDividesByZero()
    {
        Assert.Null(WeightNormalizer.WeightedAverage(new List<(decimal, decimal)>
        {
            (80m, 0m),
            (90m, -5m)
        }));
    }

    /// <summary>
    /// Existing semantics preserved deliberately: when every contribution has score but
    /// zero weight, ScorecardScoreCalculator falls back to the plain average instead of
    /// null. Pre-entry weights are validated > 0, so this only guards legacy rows.
    /// Test documents the behavior rather than changing it.
    /// </summary>
    [Fact]
    public void ScorecardCalculator_AllZeroWeights_FallsBackToPlainAverage()
    {
        var score = scorecardCalculator.Calculate(new List<ScorecardScoreContribution>
        {
            new(80m, 0m),
            new(90m, 0m)
        });

        Assert.Equal(85m, score!.Value);
    }

    [Fact]
    public void DivisionPeriodOverall_UsesSameProportionalNormalization()
    {
        var overall = scorecardCalculator.Calculate(new List<ScorecardScoreContribution>
        {
            new(80m, 40m),
            new(90m, 30m),
            new(70m, 20m)
        });

        Assert.Equal(7300m / 90m, overall!.Value);
    }

    /// <summary>
    /// Snapshot engine integration (FR-604 roll-up): an inactive goal contributes
    /// neither score nor normalization weight, its own goal score is still computed,
    /// and assigned goal weights remain untouched by calculation.
    /// </summary>
    [Fact]
    public async Task RecalculateScorecard_ExcludesInactiveGoal_FromRollup_KeepsAssignedWeights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);
        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));
        await lease.Context.SaveChangesAsync();
        var periods = await lease.Context.FiscalPeriods.Where(p => p.FiscalYearId == year.Id).OrderBy(p => p.FiscalMonth).ToListAsync();
        var month1 = periods[0];

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "Corporate 2026", Scope = ScorecardScope.Corporate };
        var activeGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Active Goal", Weight = 40m, IsActive = true };
        var inactiveGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Inactive Goal", Weight = 60m, IsActive = false };
        var activeKra = new Kra { Id = Guid.NewGuid(), GoalId = activeGoal.Id, ScorecardId = scorecard.Id, Name = "Sales", Weight = 100m };
        var inactiveKra = new Kra { Id = Guid.NewGuid(), GoalId = inactiveGoal.Id, ScorecardId = scorecard.Id, Name = "Ops", Weight = 100m };
        var activeKpi = new Kpi
        {
            Id = Guid.NewGuid(), ScorecardId = scorecard.Id, KraId = activeKra.Id,
            Name = "Active KPI", Weight = 100m, IsActive = true,
            OptimizationType = KpiOptimizationType.Maximize, KpiType = KpiType.Lagging
        };
        var inactiveKpi = new Kpi
        {
            Id = Guid.NewGuid(), ScorecardId = scorecard.Id, KraId = inactiveKra.Id,
            Name = "Inactive KPI", Weight = 100m, IsActive = true,
            OptimizationType = KpiOptimizationType.Maximize, KpiType = KpiType.Lagging
        };

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.AddRange(activeGoal, inactiveGoal);
        lease.Context.Kras.AddRange(activeKra, inactiveKra);
        lease.Context.Kpis.AddRange(activeKpi, inactiveKpi);
        lease.Context.KpiTargets.AddRange(
            new KpiTarget { KpiId = activeKpi.Id, FiscalPeriodId = month1.Id, TargetValue = 100m, Granularity = TargetGranularity.Monthly },
            new KpiTarget { KpiId = inactiveKpi.Id, FiscalPeriodId = month1.Id, TargetValue = 100m, Granularity = TargetGranularity.Monthly });
        lease.Context.KpiActualValues.AddRange(
            new KpiActualValue { KpiId = activeKpi.Id, FiscalPeriodId = month1.Id, Value = 80m, DataSource = KpiDataSource.Manual, AsOfDate = new DateTime(2026, 1, 10) },
            new KpiActualValue { KpiId = inactiveKpi.Id, FiscalPeriodId = month1.Id, Value = 100m, DataSource = KpiDataSource.Manual, AsOfDate = new DateTime(2026, 1, 10) });
        await lease.Context.SaveChangesAsync();

        var service = new ScoreCalculationService(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new GoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new KpiScoreSnapshotRepository(lease.Context),
            new KraScoreSnapshotRepository(lease.Context),
            new GoalScoreSnapshotRepository(lease.Context),
            new ScorecardScoreSnapshotRepository(lease.Context),
            new KpiScoreCalculator(new ScoreStrategyFactory()),
            new KraScoreCalculator(),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator());

        await service.RecalculateScorecardAsync(scorecard.Id, month1.Id);

        // Roll-up: only the active goal is in the denominator -> scorecard score is 80,
        // not the 92 that including the 100-scoring inactive goal would produce.
        var scorecardSnapshot = await lease.Context.ScorecardScoreSnapshots.AsNoTracking()
            .SingleAsync(s => s.FiscalPeriodId == month1.Id);
        Assert.Equal(80m, scorecardSnapshot.Score);

        // Inactive goal still gets its own (informational) goal score: 100.
        var goalScores = await lease.Context.GoalScoreSnapshots.AsNoTracking()
            .ToDictionaryAsync(s => s.GoalId, s => s.Score);
        Assert.Equal(80m, goalScores[activeGoal.Id]);
        Assert.Equal(100m, goalScores[inactiveGoal.Id]);

        // Assigned weights are never rewritten by calculation.
        var stored = await lease.Context.Goals.AsNoTracking().ToDictionaryAsync(g => g.Name, g => g.Weight);
        Assert.Equal(40m, stored["Active Goal"]);
        Assert.Equal(60m, stored["Inactive Goal"]);

        // Re-activating the goal changes the next roll-up: (80*40 + 100*60) / 100 = 92.
        var trackedGoal = await lease.Context.Goals.SingleAsync(g => g.Id == inactiveGoal.Id);
        trackedGoal.IsActive = true;
        await lease.Context.SaveChangesAsync();

        await service.RecalculateScorecardAsync(scorecard.Id, month1.Id);
        var latest = await lease.Context.ScorecardScoreSnapshots.AsNoTracking()
            .Where(s => s.FiscalPeriodId == month1.Id)
            .OrderBy(s => s.CalculatedAt)
            .LastAsync();
        Assert.Equal(92m, latest.Score);
    }
}
