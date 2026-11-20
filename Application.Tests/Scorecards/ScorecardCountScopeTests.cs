using Application.CQRS.Scorecard.Handlers.Queries;
using Application.CQRS.Scorecard.Requests.Queries;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;

namespace Application.Tests.Scorecards;

public class ScorecardCountScopeTests
{
    private sealed record Fixture(Guid ScorecardId);

    private static async Task<Fixture> CreateFixture(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "ECX Corporate Strategy",
            ScorecardStatus = ScorecardStatus.Active
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow revenue", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Trade value", Weight = 100 };
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

        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.AddRange(corporateKpi, divisionKpi);
        lease.Context.Divisions.Add(division);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        await lease.Context.SaveChangesAsync();

        return new Fixture(scorecard.Id);
    }

    [Fact]
    public async Task Scorecard_list_counts_only_corporate_goals_kras_and_kpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);

        var handler = new GetAllScorecardQueryHandler(
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KraRepository(lease.Context),
            new KpiRepository(lease.Context));

        var response = await handler.Handle(new GetAllScorecardQueryRequest(), CancellationToken.None);

        var dto = Assert.Single(response.Data!);
        Assert.Equal(1, dto.GoalCount);
        Assert.Equal(1, dto.KraCount);
        Assert.Equal(1, dto.KpiCount);
    }

    [Fact]
    public async Task Scorecard_detail_counts_only_corporate_kpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);

        var handler = new GetScorecardByIdQueryHandler(
            new ScorecardRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KraRepository(lease.Context),
            new KpiRepository(lease.Context));

        var response = await handler.Handle(
            new GetScorecardByIdQueryRequest { Id = fixture.ScorecardId }, CancellationToken.None);

        var dto = response.Data!;
        Assert.Equal(1, dto.GoalCount);
        Assert.Equal(1, dto.KraCount);
        Assert.Equal(1, dto.KpiCount);
    }
}
