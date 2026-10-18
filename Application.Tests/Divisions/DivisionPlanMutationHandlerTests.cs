using Application.CQRS.Division.Handlers.Commands;
using Application.CQRS.Division.Handlers.Queries;
using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Divisions;

public class DivisionPlanMutationHandlerTests
{
    private static readonly Guid divisionId = Guid.NewGuid();
    private static readonly Guid userId = Guid.NewGuid();

    private sealed record Fixture(Guid FiscalYearId, Guid ScorecardId, Guid GoalAId, Guid GoalBId, Guid PeriodId);

    private static async Task<Fixture> CreateFixture(TestDbContextLease lease)
    {
        var service = new FiscalCalendarService();
        var year = service.CreateFiscalYear(2026);
        var periods = service.GeneratePeriods(year);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "ECX Corporate Strategy 2026", Scope = ScorecardScope.Corporate };
        var goalA = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 50 };
        var goalB = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Customer Satisfaction", Weight = 50 };
        var division = new Domain.Entities.Division { Id = divisionId, Name = "Retail Operations", Code = "RET" };

        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.AddRange(goalA, goalB);
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();

        return new Fixture(year.Id, scorecard.Id, goalA.Id, goalB.Id, periods[0].Id);
    }

    private static SetDivisionContributionsCommandHandler CreateContributionsHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static CreateDivisionGoalCommandHandler CreateDivisionGoalHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static CreateDivisionKpiCommandHandler CreateDivisionKpiHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static UpdateDivisionGoalCommandHandler CreateUpdateGoalHandler(TestDbContextLease lease)
        => new(
            new DivisionGoalRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static DeleteDivisionGoalCommandHandler CreateDeleteGoalHandler(TestDbContextLease lease)
        => new(
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static UpdateDivisionKpiCommandHandler CreateUpdateKpiHandler(TestDbContextLease lease)
        => new(
            new KpiRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static DeleteDivisionKpiCommandHandler CreateDeleteKpiHandler(TestDbContextLease lease)
        => new(
            new KpiRepository(lease.Context),
            new DivisionGoalRepository(lease.Context));

    private static GetDivisionPlanContextQueryHandler CreateContextHandler(TestDbContextLease lease)
        => new(
            new ScorecardRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new GoalRepository(lease.Context),
            CreatePeriodService(lease),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new TestRagThresholdService());

    private static DivisionPeriodCalculationService CreatePeriodService(TestDbContextLease lease)
        => new(
            new DivisionGoalRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new KpiScoreCalculator(new ScoreStrategyFactory()),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            TestBusinessClock.Real());

    // FR-904: builds a cumulative target set that satisfies the reconciliation rules.
    private static async Task<List<DivisionKpiTargetInputDto>> BuildReconciledTargetsAsync(
        TestDbContextLease lease, Guid fiscalYearId)
    {
        var periods = await lease.Context.FiscalPeriods
            .Where(p => p.FiscalYearId == fiscalYearId)
            .OrderBy(p => p.FiscalMonth)
            .ToListAsync();

        var targets = periods
            .Select(p => new DivisionKpiTargetInputDto
            {
                FiscalPeriodId = p.Id,
                TargetValue = 5m,
                Granularity = TargetGranularity.Monthly
            })
            .ToList();

        targets.AddRange(periods.Where(p => p.IsQuarterEnd).Select(p => new DivisionKpiTargetInputDto
        {
            FiscalPeriodId = p.Id,
            TargetValue = 15m,
            Granularity = TargetGranularity.Quarterly
        }));

        targets.Add(new DivisionKpiTargetInputDto
        {
            FiscalPeriodId = periods.Single(p => p.IsYearEnd).Id,
            TargetValue = 60m,
            Granularity = TargetGranularity.Annual
        });

        return targets;
    }

    private static async Task<Guid> CreatePlanWithContributionsAsync(TestDbContextLease lease, Fixture fixture)
    {
        var handler = CreateContributionsHandler(lease);
        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId }
        }, CancellationToken.None);
        return response.Data!.Id;
    }

    private static async Task<Guid> CreateGoalWithKpiAsync(TestDbContextLease lease, Fixture fixture)
    {
        var goalResponse = await CreateDivisionGoalHandler(lease).Handle(
            new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 100
            }, CancellationToken.None);

        await CreateDivisionKpiHandler(lease).Handle(
            new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goalResponse.Data!.Id,
                Name = "New Branches",
                Weight = 100,
                KpiType = KpiType.Lagging
            }, CancellationToken.None);

        return goalResponse.Data.Id;
    }

    // FR-904: like CreateGoalWithKpiAsync but also seeds a reconciled cumulative target set,
    // which is what a finalizable (target-carrying) division KPI looks like.
    private static async Task<Guid> CreateGoalWithReconciledKpiAsync(TestDbContextLease lease, Fixture fixture)
    {
        var goalResponse = await CreateDivisionGoalHandler(lease).Handle(
            new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 100
            }, CancellationToken.None);

        await CreateDivisionKpiHandler(lease).Handle(
            new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goalResponse.Data!.Id,
                Name = "New Branches",
                Weight = 100,
                IsCumulative = true,
                Targets = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId)
            }, CancellationToken.None);

        return goalResponse.Data.Id;
    }

    private static async Task FinalizePlanAsync(TestDbContextLease lease)
    {
        var plan = await lease.Context.DivisionPlans.SingleAsync();
        plan.PlanStatus = DivisionPlanStatus.Finalized;
        await lease.Context.SaveChangesAsync();
    }

    private static FinalizeDivisionPlanCommandHandler CreateFinalizeHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new TestCurrentUserService { UserId = userId },
            new KpiTargetRepository(lease.Context));

    // ── Update division goal ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateDivisionGoal_UpdatesNameAndWeight()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);

        var response = await CreateUpdateGoalHandler(lease).Handle(
            new UpdateDivisionGoalCommandRequest
            {
                DivisionGoalId = goalId,
                Name = "Expand Retail Footprint",
                Weight = 80,
                ContributionDescription = "Drives revenue"
            }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Expand Retail Footprint", response.Data!.Name);
        Assert.Equal(80, response.Data.Weight);

        var saved = await lease.Context.DivisionGoals.SingleAsync();
        Assert.Equal("Expand Retail Footprint", saved.Name);
        Assert.Equal(80, saved.Weight);
        Assert.Equal("Drives revenue", saved.ContributionDescription);
    }

    [Fact]
    public async Task UpdateDivisionGoal_WeightExceeds100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalHandler = CreateDivisionGoalHandler(lease);
        var first = await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 60
        }, CancellationToken.None);
        var second = await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Boost Digital Sales",
            Weight = 40
        }, CancellationToken.None);

        var handler = CreateUpdateGoalHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new UpdateDivisionGoalCommandRequest
            {
                DivisionGoalId = second.Data!.Id,
                Weight = 50
            }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDivisionGoal_ParentNotContributed_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);

        var handler = CreateUpdateGoalHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new UpdateDivisionGoalCommandRequest
            {
                DivisionGoalId = goalId,
                ParentGoalId = fixture.GoalBId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDivisionGoal_FinalizedPlan_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        await FinalizePlanAsync(lease);

        var handler = CreateUpdateGoalHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new UpdateDivisionGoalCommandRequest { DivisionGoalId = goalId, Name = "Renamed" }, CancellationToken.None));
    }

    // ── Delete division goal ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteDivisionGoal_RemovesGoalAndItsKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpiId = (await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId)).Id;

        var result = await CreateDeleteGoalHandler(lease).Handle(
            new DeleteDivisionGoalCommandRequest { DivisionGoalId = goalId }, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(0, await lease.Context.DivisionGoals.CountAsync(g => g.Id == goalId));
        Assert.Equal(1, await lease.Context.DivisionGoals.IgnoreQueryFilters().CountAsync(g => g.Id == goalId && g.IsDeleted));
        Assert.True(await lease.Context.Kpis.IgnoreQueryFilters().AnyAsync(k => k.Id == kpiId && k.IsDeleted));
    }

    [Fact]
    public async Task DeleteDivisionGoal_FinalizedPlan_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        await FinalizePlanAsync(lease);

        var handler = CreateDeleteGoalHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new DeleteDivisionGoalCommandRequest { DivisionGoalId = goalId }, CancellationToken.None));
    }

    // ── Update division KPI ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateDivisionKpi_UpdatesFields()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpiId = (await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId)).Id;

        var response = await CreateUpdateKpiHandler(lease).Handle(
            new UpdateDivisionKpiCommandRequest
            {
                DivisionKpiId = kpiId,
                Name = "New Branches Opened",
                Weight = 70,
                Unit = "count"
            }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("New Branches Opened", response.Data!.Name);
        Assert.Equal(70, response.Data.Weight);

        var saved = await lease.Context.Kpis.SingleAsync();
        Assert.Equal("New Branches Opened", saved.Name);
        Assert.Equal(70, saved.Weight);
    }

    [Fact]
    public async Task UpdateDivisionKpi_WeightExceeds100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goal = await CreateDivisionGoalHandler(lease).Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var kpiHandler = CreateDivisionKpiHandler(lease);
        var first = await kpiHandler.Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goal.Data!.Id,
            Name = "New Branches",
            Weight = 60
        }, CancellationToken.None);
        var second = await kpiHandler.Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goal.Data.Id,
            Name = "Renewals",
            Weight = 40
        }, CancellationToken.None);

        var handler = CreateUpdateKpiHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new UpdateDivisionKpiCommandRequest
            {
                DivisionKpiId = second.Data!.Id,
                Weight = 50
            }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDivisionKpi_NotADivisionKpi_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        // A KPI not belonging to a division goal is a corporate KPI sitting under a KRA.
        var corporateGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = fixture.ScorecardId, Name = "Plan Integrity Goal", Weight = 50 };
        var corporateKra = new Kra { Id = Guid.NewGuid(), ScorecardId = fixture.ScorecardId, GoalId = corporateGoal.Id, Name = "Plan Integrity KRA", Weight = 50 };
        var standalone = new Kpi { Id = Guid.NewGuid(), ScorecardId = fixture.ScorecardId, KraId = corporateKra.Id, Name = "Standalone", Weight = 10 };
        lease.Context.Goals.Add(corporateGoal);
        lease.Context.Kras.Add(corporateKra);
        lease.Context.Kpis.Add(standalone);
        await lease.Context.SaveChangesAsync();

        var handler = CreateUpdateKpiHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new UpdateDivisionKpiCommandRequest { DivisionKpiId = standalone.Id, Weight = 20 }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDivisionKpi_ReplacesTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpi = await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId);
        kpi.IsCumulative = true;
        await lease.Context.SaveChangesAsync();

        var response = await CreateUpdateKpiHandler(lease).Handle(
            new UpdateDivisionKpiCommandRequest
            {
                DivisionKpiId = kpi.Id,
                Targets = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId)
            }, CancellationToken.None);

        Assert.True(response.Success);
        var savedTargets = await lease.Context.KpiTargets.Where(t => t.KpiId == kpi.Id).ToListAsync();
        Assert.Equal(17, savedTargets.Count);
        Assert.Equal(60m, savedTargets.Single(t => t.Granularity == TargetGranularity.Annual).TargetValue);

        // A second update with a fresh set replaces (not appends) the previous targets.
        var replacements = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId);
        foreach (var t in replacements) t.TargetValue = 10m * (t.Granularity == TargetGranularity.Annual ? 12 : t.Granularity == TargetGranularity.Quarterly ? 3 : 1);
        await CreateUpdateKpiHandler(lease).Handle(
            new UpdateDivisionKpiCommandRequest { DivisionKpiId = kpi.Id, Targets = replacements },
            CancellationToken.None);

        savedTargets = await lease.Context.KpiTargets.Where(t => t.KpiId == kpi.Id).ToListAsync();
        Assert.Equal(17, savedTargets.Count);
        Assert.Equal(120m, savedTargets.Single(t => t.Granularity == TargetGranularity.Annual).TargetValue);
    }

    // ── Delete division KPI ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteDivisionKpi_SoftDeletes()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpi = await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId);

        var result = await CreateDeleteKpiHandler(lease).Handle(
            new DeleteDivisionKpiCommandRequest { DivisionKpiId = kpi.Id }, CancellationToken.None);

        Assert.True(result);
        Assert.False(await lease.Context.Kpis.AnyAsync(k => k.Id == kpi.Id));
        Assert.True(await lease.Context.Kpis.IgnoreQueryFilters().AnyAsync(k => k.Id == kpi.Id && k.IsDeleted));
    }

    [Fact]
    public async Task DeleteDivisionKpi_FinalizedPlan_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpi = await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId);
        await FinalizePlanAsync(lease);

        var handler = CreateDeleteKpiHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new DeleteDivisionKpiCommandRequest { DivisionKpiId = kpi.Id }, CancellationToken.None));
    }

    // ── Plan context by scorecard ─────────────────────────────────────────

    [Fact]
    public async Task GetDivisionPlanContext_NoPlan_ReturnsEmptyPlanWithAvailableGoals()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);

        var response = await CreateContextHandler(lease).Handle(
            new GetDivisionPlanContextQueryRequest { ScorecardId = fixture.ScorecardId, DivisionId = divisionId },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(DivisionPlanStatus.Draft, response.Data!.PlanStatus);
        Assert.Equal("Retail Operations", response.Data.DivisionName);
        Assert.Empty(response.Data.Contributions);
        Assert.Equal(2, response.Data.AvailableGoals.Count);

        var savedPlan = await lease.Context.DivisionPlans.FirstOrDefaultAsync();
        Assert.Null(savedPlan);
    }

    [Fact]
    public async Task GetDivisionPlanContext_WithPlan_ReturnsGoalsAndContributions()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var goalId = await CreateGoalWithKpiAsync(lease, fixture);

        var response = await CreateContextHandler(lease).Handle(
            new GetDivisionPlanContextQueryRequest { ScorecardId = fixture.ScorecardId },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(fixture.ScorecardId, response.Data!.CorporateScorecardId);
        Assert.Single(response.Data.Contributions);
        Assert.Equal(fixture.GoalAId, response.Data.Contributions.Single().GoalId);
        Assert.Contains(response.Data.AvailableGoals, g => g.GoalId == fixture.GoalBId);

        var goal = response.Data.Goals.Single();
        Assert.Equal(goalId, goal.Id);
        Assert.Single(goal.Kpis);
    }

    [Fact]
    public async Task GetDivisionPlanContext_WithTargets_ReturnsFiscalPeriodsAndKpiTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goal = await CreateDivisionGoalHandler(lease).Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        await CreateDivisionKpiHandler(lease).Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goal.Data!.Id,
            Name = "New Branches",
            Weight = 100,
            IsCumulative = true,
            Targets = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId)
        }, CancellationToken.None);

        var response = await CreateContextHandler(lease).Handle(
            new GetDivisionPlanContextQueryRequest { ScorecardId = fixture.ScorecardId },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(12, response.Data!.FiscalPeriods.Count);
        var kpi = response.Data.Goals.Single().Kpis.Single();
        Assert.Equal(17, kpi.Targets.Count);
        Assert.Contains(kpi.Targets, t => t.Granularity == TargetGranularity.Annual && t.TargetValue == 60m);
    }

    // ── Finalize division plan ────────────────────────────────────────────

    [Fact]
    public async Task FinalizeDivisionPlan_FinalizesDraftPlan()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        await CreateGoalWithReconciledKpiAsync(lease, fixture);

        var response = await CreateFinalizeHandler(lease).Handle(
            new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(DivisionPlanStatus.Finalized, response.Data!.PlanStatus);
        Assert.Equal(1, response.Data.GoalCount);
        Assert.Equal(1, response.Data.KpiCount);

        var saved = await lease.Context.DivisionPlans.SingleAsync();
        Assert.Equal(DivisionPlanStatus.Finalized, saved.PlanStatus);
        Assert.NotNull(saved.FinalizedAtUtc);
        Assert.Equal(userId, saved.FinalizedByUserId);
    }

    [Fact]
    public async Task FinalizeDivisionPlan_KpiWithoutTargets_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        await CreateGoalWithKpiAsync(lease, fixture);

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_SomeKpisMissingTargets_ThrowsBadRequestNamingThem()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalResponse = await CreateDivisionGoalHandler(lease).Handle(
            new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 100
            }, CancellationToken.None);
        var goalId = goalResponse.Data!.Id;

        await CreateDivisionKpiHandler(lease).Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goalId,
            Name = "New Branches",
            Weight = 60,
            IsCumulative = true,
            Targets = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId)
        }, CancellationToken.None);

        await CreateDivisionKpiHandler(lease).Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goalId,
            Name = "Revenue Per Customer",
            Weight = 40
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateFinalizeHandler(lease).Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));

        Assert.Contains("Revenue Per Customer", ex.Message);
        Assert.DoesNotContain("New Branches", ex.Message);
    }

    [Fact]
    public async Task FinalizeDivisionPlan_NoPlan_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_AlreadyFinalized_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        await CreateGoalWithKpiAsync(lease, fixture);
        await FinalizePlanAsync(lease);

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_NoGoals_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_GoalWeightsExceed100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        // Seed goals directly: the create-goal handler already blocks >100%, so this
        // simulates legacy data that slipped into a draft plan.
        var planId = (await lease.Context.DivisionPlans.SingleAsync()).Id;
        lease.Context.DivisionGoals.AddRange(
            new DivisionGoal
            {
                Id = Guid.NewGuid(),
                DivisionPlanId = planId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 60
            },
            new DivisionGoal
            {
                Id = Guid.NewGuid(),
                DivisionPlanId = planId,
                ParentGoalId = fixture.GoalAId,
                Name = "Boost Digital Sales",
                Weight = 50
            });
        await lease.Context.SaveChangesAsync();

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task SetContributions_DeselectWithCascade_RemovesDivisionGoalsAndKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var handler = CreateContributionsHandler(lease);

        await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId, fixture.GoalBId }
        }, CancellationToken.None);

        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpiId = (await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId)).Id;

        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalBId },
            CascadeRemoveDependents = true
        }, CancellationToken.None);

        Assert.Single(response.Data!.Contributions);
        Assert.Equal(fixture.GoalBId, response.Data.Contributions.Single().GoalId);
        Assert.True(await lease.Context.DivisionGoals.IgnoreQueryFilters().AnyAsync(g => g.Id == goalId && g.IsDeleted));
        Assert.True(await lease.Context.Kpis.IgnoreQueryFilters().AnyAsync(k => k.Id == kpiId && k.IsDeleted));
    }

    [Fact]
    public async Task SetContributions_DeselectKeep_LeavesDivisionGoalsAndKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var handler = CreateContributionsHandler(lease);

        await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId }
        }, CancellationToken.None);

        var goalId = await CreateGoalWithKpiAsync(lease, fixture);
        var kpiId = (await lease.Context.Kpis.SingleAsync(k => k.DivisionGoalId == goalId)).Id;

        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid>(),
            CascadeRemoveDependents = false
        }, CancellationToken.None);

        Assert.Empty(response.Data!.Contributions);
        Assert.False(await lease.Context.DivisionGoals.IgnoreQueryFilters().AnyAsync(g => g.Id == goalId && g.IsDeleted));
        Assert.False(await lease.Context.Kpis.IgnoreQueryFilters().AnyAsync(k => k.Id == kpiId && k.IsDeleted));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_GoalWeightsBelow100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var planId = (await lease.Context.DivisionPlans.SingleAsync()).Id;
        lease.Context.DivisionGoals.Add(new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = planId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 80
        });
        await lease.Context.SaveChangesAsync();

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_KpiWeightsBelow100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalResponse = await CreateDivisionGoalHandler(lease).Handle(
            new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 100
            }, CancellationToken.None);

        await CreateDivisionKpiHandler(lease).Handle(
            new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goalResponse.Data!.Id,
                Name = "New Branches",
                Weight = 90
            }, CancellationToken.None);

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FinalizeDivisionPlan_NoKpis_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        await CreateDivisionGoalHandler(lease).Handle(
            new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 100
            }, CancellationToken.None);

        var handler = CreateFinalizeHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None));
    }
}