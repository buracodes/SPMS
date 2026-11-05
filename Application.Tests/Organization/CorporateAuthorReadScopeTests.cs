using Application.CQRS.Division.Handlers.Queries;
using Application.CQRS.Division.Requests.Queries;
using Application.CQRS.Kpi.Handlers.Queries;
using Application.CQRS.Kpi.Requests.Queries;
using Application.CQRS.ScoreSnapshot.Handlers.Queries;
using Application.CQRS.ScoreSnapshot.Requests.Queries;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;

namespace Application.Tests.Organization;

/// <summary>
/// ECX accounts are mapped to a division straight from the HR organization hierarchy, so an
/// admin or power user usually carries a division claim as well as their corporate rights. That
/// claim must not remove the corporate scorecard from under them: the management page reads the
/// scorecard snapshots, opens KPI details to assign owners and lists divisions to pick one.
/// A principal without corporate-wide visibility stays narrowed to its own division.
/// </summary>
public class CorporateAuthorReadScopeTests
{
    private static async Task<Fixture> SeedAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "ECX Corporate 2026/27",
            Scope = ScorecardScope.Corporate,
            ScorecardStatus = ScorecardStatus.Active
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow trade value", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Orders", Weight = 100 };
        var corporateKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Orders executed",
            Unit = "orders",
            Weight = 100,
            IsCumulative = true,
            IsActive = true,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        var plannedDivision = new Division { Id = Guid.NewGuid(), Name = "Trading", Code = "TRD" };
        var otherDivision = new Division { Id = Guid.NewGuid(), Name = "Clearing", Code = "CLR" };
        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = plannedDivision.Id,
            ScorecardId = scorecard.Id,
            PlanStatus = DivisionPlanStatus.Draft
        };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = goal.Id,
            Name = "Trading network",
            Weight = 100
        };
        var divisionKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            DivisionOwnerId = otherDivision.Id,
            Name = "Branch orders",
            Unit = "orders",
            Weight = 100,
            IsCumulative = true,
            IsActive = true,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var plannedDivisionKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            DivisionOwnerId = plannedDivision.Id,
            Name = "Trading desk orders",
            Unit = "orders",
            Weight = 100,
            IsCumulative = true,
            IsActive = true,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Divisions.AddRange(plannedDivision, otherDivision);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        lease.Context.Kpis.AddRange(corporateKpi, divisionKpi, plannedDivisionKpi);
        await lease.Context.SaveChangesAsync();

        return new Fixture(
            year.Id,
            scorecard.Id,
            kra.Id,
            corporateKpi.Id,
            divisionKpi.Id,
            plannedDivisionKpi.Id,
            plannedDivision.Id,
            otherDivision.Id,
            plan.Id,
            divisionGoal.Id);
    }

    private sealed record Fixture(
        Guid FiscalYearId,
        Guid ScorecardId,
        Guid KraId,
        Guid CorporateKpiId,
        Guid DivisionKpiId,
        Guid PlannedDivisionKpiId,
        Guid PlannedDivisionId,
        Guid OtherDivisionId,
        Guid PlanId,
        Guid DivisionGoalId);

    /// <summary>
    /// A division account, in production, never holds portfolio or corporate-wide visibility:
    /// both resolve to false as soon as a division claim exists.
    /// </summary>
    private static TestOrganizationalScopeService DivisionAccount(Guid divisionId) => new()
    {
        DivisionIds = new[] { divisionId },
        HasCorporateAccess = false,
        HasDivisionPortfolioAccess = false
    };

    private static TestOrganizationalScopeService CorporateAuthorWithDivisionClaim(Guid divisionId) => new()
    {
        DivisionIds = new[] { divisionId },
        HasCorporateAccess = true,
        HasDivisionPortfolioAccess = false
    };

    private static GetScoreSnapshotsQueryHandler CreateSnapshotHandler(
        TestDbContextLease lease,
        IOrganizationalScopeService scope)
    {
        var context = lease.Context;
        var factory = new ScoreStrategyFactory();
        var clock = TestBusinessClock.Real();

        return new GetScoreSnapshotsQueryHandler(
            new ScorecardRepository(context),
            new ScorePeriodCalculationService(
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
                clock),
            new DivisionPlanRepository(context),
            new DivisionPeriodCalculationService(
                new DivisionGoalRepository(context),
                new FiscalYearRepository(context),
                new FiscalPeriodRepository(context),
                new KpiTargetRepository(context),
                new KpiActualValueRepository(context),
                new KpiScoreCalculator(factory),
                new GoalScoreCalculator(),
                new ScorecardScoreCalculator(),
                clock),
            scope);
    }

    private static GetScoreSnapshotsQueryRequest SnapshotRequest(Fixture fixture)
        => new()
        {
            ScorecardId = fixture.ScorecardId,
            FiscalYearId = fixture.FiscalYearId,
            PeriodType = PeriodType.Year
        };

    [Fact]
    public async Task Snapshots_CorporateAuthorWithDivisionClaim_ReadsTheCorporateScorecard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        // The claim is a division that does not plan on this scorecard at all.
        var handler = CreateSnapshotHandler(lease, CorporateAuthorWithDivisionClaim(fixture.OtherDivisionId));

        var response = await handler.Handle(
            SnapshotRequest(fixture), CancellationToken.None);

        var kpiIds = response.Data!.KpiSnapshots.Select(s => s.EntityId).ToHashSet();
        Assert.Contains(fixture.CorporateKpiId, kpiIds);
        Assert.DoesNotContain(fixture.DivisionKpiId, kpiIds);
        Assert.Contains(fixture.KraId, response.Data.KraSnapshots.Select(s => s.EntityId));
    }

    [Fact]
    public async Task Snapshots_DivisionAccount_ReadsOnlyItsOwnPlan()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var handler = CreateSnapshotHandler(lease, DivisionAccount(fixture.PlannedDivisionId));

        var response = await handler.Handle(
            SnapshotRequest(fixture), CancellationToken.None);

        var kpiIds = response.Data!.KpiSnapshots.Select(s => s.EntityId).ToHashSet();
        Assert.Contains(fixture.DivisionKpiId, kpiIds);
        Assert.Contains(fixture.PlannedDivisionKpiId, kpiIds);
        Assert.DoesNotContain(fixture.CorporateKpiId, kpiIds);
        Assert.Empty(response.Data.KraSnapshots);
    }

    [Fact]
    public async Task Snapshots_DivisionAccountWithoutPlanOnScorecard_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var handler = CreateSnapshotHandler(lease, DivisionAccount(fixture.OtherDivisionId));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            SnapshotRequest(fixture), CancellationToken.None));
    }

    [Fact]
    public async Task KpiDetail_CorporateAuthorWithDivisionClaim_OpensAnyKpi()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var handler = new GetKpiByIdQueryHandler(
            new KpiRepository(lease.Context), CorporateAuthorWithDivisionClaim(fixture.PlannedDivisionId));

        var response = await handler.Handle(
            new GetKpiByIdQueryRequest { Id = fixture.DivisionKpiId }, CancellationToken.None);

        Assert.Equal(fixture.DivisionKpiId, response.Data.Id);
        Assert.Equal(fixture.OtherDivisionId, response.Data.DivisionOwnerId);
    }

    [Fact]
    public async Task KpiDetail_DivisionAccount_StaysOnKpisAssignedToItsDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var handler = new GetKpiByIdQueryHandler(
            new KpiRepository(lease.Context), DivisionAccount(fixture.PlannedDivisionId));

        var assigned = await handler.Handle(
            new GetKpiByIdQueryRequest { Id = fixture.PlannedDivisionKpiId }, CancellationToken.None);
        Assert.Equal(fixture.PlannedDivisionKpiId, assigned.Data.Id);

        // Owned by another division, and an unassigned corporate KPI, both stay out of reach.
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new GetKpiByIdQueryRequest { Id = fixture.DivisionKpiId }, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new GetKpiByIdQueryRequest { Id = fixture.CorporateKpiId }, CancellationToken.None));
    }

    [Fact]
    public async Task Divisions_CorporateAuthorWithDivisionClaim_ListsEveryActiveDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var handler = new GetDivisionsQueryHandler(
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            CorporateAuthorWithDivisionClaim(fixture.PlannedDivisionId));

        var response = await handler.Handle(new GetDivisionsQueryRequest(), CancellationToken.None);

        Assert.Equal(
            new[] { "Clearing", "Trading" },
            response.Data.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Divisions_DivisionAccount_SeesOnlyItsOwn()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var handler = new GetDivisionsQueryHandler(
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            DivisionAccount(fixture.PlannedDivisionId));

        var response = await handler.Handle(new GetDivisionsQueryRequest(), CancellationToken.None);

        Assert.Equal(new[] { "Trading" }, response.Data.Select(d => d.Name).ToArray());
    }
}
