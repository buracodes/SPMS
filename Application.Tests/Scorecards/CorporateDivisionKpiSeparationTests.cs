using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;

namespace Application.Tests.Scorecards;

public class CorporateDivisionKpiSeparationTests
{
    [Fact]
    public async Task Corporate_period_calculation_excludes_division_owned_kpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var context = lease.Context;

        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);
        context.FiscalYears.Add(year);
        context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "ECX Corporate Strategy",
            Scope = ScorecardScope.Corporate,
            ScorecardStatus = ScorecardStatus.Active
        };
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            Name = "Grow revenue",
            Weight = 100
        };
        var kra = new Kra
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            ScorecardId = scorecard.Id,
            Name = "Trade value",
            Weight = 100
        };
        var corporateKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Corporate trade value",
            Weight = 100,
            IsActive = true
        };

        var division = new Division { Id = Guid.NewGuid(), Name = "Retail Operations", Code = "RET" };
        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = division.Id,
            ScorecardId = scorecard.Id,
            PlanStatus = DivisionPlanStatus.Draft
        };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = goal.Id,
            Name = "Retail network",
            Weight = 100
        };
        var divisionKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            Name = "Branches opened",
            Weight = 100,
            IsActive = true
        };

        context.Scorecards.Add(scorecard);
        context.Goals.Add(goal);
        context.Kras.Add(kra);
        context.Kpis.AddRange(corporateKpi, divisionKpi);
        context.Divisions.Add(division);
        context.DivisionPlans.Add(plan);
        context.DivisionGoals.Add(divisionGoal);
        await context.SaveChangesAsync();

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

        var result = await service.CalculateAsync(
            scorecard.Id, year.Id, PeriodType.Year, null, null, null, null);

        Assert.True(result.Kpis.ContainsKey(corporateKpi.Id));
        Assert.False(result.Kpis.ContainsKey(divisionKpi.Id));
        Assert.True(result.Kras.ContainsKey(kra.Id));
        Assert.True(result.Goals.ContainsKey(goal.Id));
        Assert.True(result.Scorecards.ContainsKey(scorecard.Id));
    }
}
