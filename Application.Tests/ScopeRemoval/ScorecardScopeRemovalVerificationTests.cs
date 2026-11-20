using System.Reflection;
using System.Text.Json;
using API.Authorization;
using API.Controllers;
using Application.CQRS.Division.Handlers.Commands;
using Application.CQRS.Division.Handlers.Queries;
using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Dashboard;
using Application.DTOS.PerformanceMonitoring;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.ScopeRemoval;

/// <summary>
/// Focused regression tests for removing Scorecard.Scope. Corporate-versus-division
/// separation is enforced by data and access (DivisionPlan keys, division claims,
/// Kpi.DivisionGoalId and separate controllers), never by a scorecard marker.
/// </summary>
public sealed class ScorecardScopeRemovalVerificationTests
{
    private sealed record ScopeFixture(
        Guid FiscalYearId,
        Guid ScorecardId,
        Guid OtherScorecardId,
        Guid GoalId,
        Guid OtherGoalId,
        Guid DivisionAId,
        Guid DivisionBId);

    [Fact]
    public async Task Fr301_Multiple_independent_scorecards_can_share_one_fiscal_year()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = new FiscalCalendarService().CreateFiscalYear(2099);

        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.AddRange(
            NewScorecard(year.Id, "Corporate Strategy"),
            NewScorecard(year.Id, "Digital Transformation"));
        await lease.Context.SaveChangesAsync();

        var scorecards = await lease.Context.Scorecards
            .OrderBy(s => s.Name)
            .ToListAsync();

        Assert.Equal(2, scorecards.Count);
        Assert.All(scorecards, s => Assert.Equal(year.Id, s.FiscalYearId));
        Assert.Equal(new[] { "Corporate Strategy", "Digital Transformation" },
            scorecards.Select(s => s.Name));
    }

    [Fact]
    public async Task Division_plan_identity_is_the_division_and_selected_scorecard_and_is_unique()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = new FiscalCalendarService().CreateFiscalYear(2099);
        var scorecard = NewScorecard(year.Id, "Corporate Strategy");
        var division = new Domain.Entities.Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };

        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();

        lease.Context.DivisionPlans.AddRange(
            NewPlan(division.Id, scorecard.Id),
            NewPlan(division.Id, scorecard.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Plan_writes_accept_only_goals_from_the_explicitly_selected_scorecard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var scope = new TestOrganizationalScopeService();
        var handler = CreateContributionsHandler(lease, scope);

        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = fixture.DivisionAId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalId }
        }, CancellationToken.None);

        Assert.True(response.Success);
        var plan = await lease.Context.DivisionPlans.SingleAsync();
        Assert.Equal(fixture.DivisionAId, plan.DivisionId);
        Assert.Equal(fixture.ScorecardId, plan.ScorecardId);

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new SetDivisionContributionsCommandRequest
            {
                DivisionId = fixture.DivisionAId,
                ScorecardId = fixture.ScorecardId,
                GoalIds = new List<Guid> { fixture.OtherGoalId }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Omitting_division_with_multiple_plans_requires_an_explicit_division()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        lease.Context.DivisionPlans.AddRange(
            NewPlan(fixture.DivisionAId, fixture.ScorecardId),
            NewPlan(fixture.DivisionBId, fixture.ScorecardId));
        await lease.Context.SaveChangesAsync();

        var scope = new TestOrganizationalScopeService
        {
            DivisionId = null,
            HasCorporateAccess = true,
            HasDivisionPortfolioAccess = true
        };
        var handler = CreateContextHandler(lease, scope);

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new GetDivisionPlanContextQueryRequest { ScorecardId = fixture.ScorecardId },
            CancellationToken.None));
    }

    [Fact]
    public async Task Closed_scorecard_rejects_division_writes_but_allows_plan_reads()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease, closedScorecard: true);
        var scope = new TestOrganizationalScopeService
        {
            DivisionId = fixture.DivisionAId,
            HasCorporateAccess = false
        };

        await Assert.ThrowsAsync<ScorecardClosedException>(() => CreateContributionsHandler(lease, scope).Handle(
            new SetDivisionContributionsCommandRequest
            {
                DivisionId = fixture.DivisionAId,
                ScorecardId = fixture.ScorecardId,
                GoalIds = new List<Guid> { fixture.GoalId }
            }, CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() => CreateDivisionGoalHandler(lease, scope).Handle(
            new CreateDivisionGoalCommandRequest
            {
                DivisionId = fixture.DivisionAId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalId,
                Name = "Closed scorecard goal",
                Weight = 100
            }, CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() => CreateDivisionKpiHandler(lease, scope).Handle(
            new CreateDivisionKpiCommandRequest
            {
                DivisionId = fixture.DivisionAId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = Guid.NewGuid(),
                Name = "Closed scorecard KPI",
                Weight = 100
            }, CancellationToken.None));

        var read = await CreateContextHandler(lease, scope).Handle(
            new GetDivisionPlanContextQueryRequest
            {
                DivisionId = fixture.DivisionAId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None);

        Assert.True(read.Success);
        Assert.Equal(fixture.ScorecardId, read.Data.ScorecardId);
        Assert.Equal(fixture.ScorecardId, read.Data.CorporateScorecardId);
    }

    [Fact]
    public async Task Explicit_division_outside_the_callers_claim_is_rejected_not_replaced()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease);
        var scope = new TestOrganizationalScopeService
        {
            DivisionId = fixture.DivisionAId,
            HasCorporateAccess = false
        };

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateContributionsHandler(lease, scope).Handle(
            new SetDivisionContributionsCommandRequest
            {
                DivisionId = fixture.DivisionBId,
                ScorecardId = fixture.ScorecardId,
                GoalIds = new List<Guid> { fixture.GoalId }
            }, CancellationToken.None));
    }

    [Fact]
    public void Scorecard_contracts_expose_no_scope_marker_or_division_ownership()
    {
        Type[] scorecardContracts =
        {
            typeof(Scorecard),
            typeof(ScorecardDto),
            typeof(GetAllScorecardDto),
            typeof(CreateScorecardDto),
            typeof(UpdateScorecardDto),
            typeof(CloneScorecardResultDto),
            typeof(DivisionPlan)
        };

        foreach (var type in scorecardContracts)
        {
            Assert.DoesNotContain(type.GetProperties(),
                property => property.Name.Equals("Scope", StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(typeof(Scorecard).GetProperties(),
            property => property.Name.Contains("Division", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(Scorecard).Assembly.GetTypes(),
            type => type.Name == "ScorecardScope");
        Assert.DoesNotContain(typeof(ScorecardDto).Assembly.GetTypes(),
            type => type.Name == "ScorecardScope");
    }

    [Fact]
    public void Intentional_data_scopes_remain_in_monitoring_and_dashboard_contracts()
    {
        var monitoringJson = JsonSerializer.Serialize(new PerformanceMonitoringDto());
        var dashboardJson = JsonSerializer.Serialize(new DashboardRoleContextDto());

        Assert.True(monitoringJson.Contains("Scope", StringComparison.Ordinal),
            "PerformanceMonitoring.scope is an intentional performance data scope.");
        Assert.True(dashboardJson.Contains("DataScope", StringComparison.Ordinal),
            "Dashboard dataScope is an intentional permission-derived data scope.");
        Assert.DoesNotContain("ScorecardScope", monitoringJson + dashboardJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Corporate_and_division_actual_entry_endpoints_remain_separate()
    {
        var corporate = typeof(KpiActualController);
        var division = typeof(DivisionController);

        Assert.Equal("api/[controller]",
            corporate.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotEqual(corporate, division);

        var corporateEnter = corporate.GetMethod(nameof(KpiActualController.Enter))!;
        var divisionEnter = division.GetMethod(nameof(DivisionController.EnterActual))!;
        var divisionRead = division.GetMethod(nameof(DivisionController.GetActuals))!;

        Assert.Equal(Permissions.ActualsEnter, PolicyOf(corporateEnter));
        Assert.Equal(Permissions.DivisionActualsEnter, PolicyOf(divisionEnter));
        Assert.Equal(AnyPermissionPolicyNames.DivisionRead, PolicyOf(divisionRead));
        Assert.DoesNotContain("division", corporateEnter.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string? PolicyOf(MethodInfo method)
        => method.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

    private static Scorecard NewScorecard(Guid fiscalYearId, string name) => new()
    {
        Id = Guid.NewGuid(),
        FiscalYearId = fiscalYearId,
        Name = name
    };

    private static DivisionPlan NewPlan(Guid divisionId, Guid scorecardId) => new()
    {
        Id = Guid.NewGuid(),
        DivisionId = divisionId,
        ScorecardId = scorecardId,
        PlanStatus = DivisionPlanStatus.Draft
    };

    private static async Task<ScopeFixture> SeedAsync(TestDbContextLease lease, bool closedScorecard = false)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2099);
        var periods = calendar.GeneratePeriods(year);

        var scorecard = NewScorecard(year.Id, "Corporate Strategy");
        if (closedScorecard)
        {
            scorecard.ScorecardStatus = ScorecardStatus.Closed;
        }

        var otherScorecard = NewScorecard(year.Id, "Digital Transformation");
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            Name = "Selected corporate goal",
            Weight = 100
        };
        var otherGoal = new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = otherScorecard.Id,
            Name = "Other scorecard corporate goal",
            Weight = 100
        };
        var divisionA = new Domain.Entities.Division
        {
            Id = Guid.NewGuid(),
            Name = "Operations",
            Code = "OPS"
        };
        var divisionB = new Domain.Entities.Division
        {
            Id = Guid.NewGuid(),
            Name = "Retail",
            Code = "RET"
        };

        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.AddRange(scorecard, otherScorecard);
        lease.Context.Goals.AddRange(goal, otherGoal);
        lease.Context.Divisions.AddRange(divisionA, divisionB);
        await lease.Context.SaveChangesAsync();

        return new ScopeFixture(
            year.Id,
            scorecard.Id,
            otherScorecard.Id,
            goal.Id,
            otherGoal.Id,
            divisionA.Id,
            divisionB.Id);
    }

    private static ScorecardClosureGuard CreateClosureGuard(TestDbContextLease lease)
        => new(
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            TestBusinessClock.Real());

    private static SetDivisionContributionsCommandHandler CreateContributionsHandler(
        TestDbContextLease lease, IOrganizationalScopeService scope)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            scope,
            CreateClosureGuard(lease));

    private static CreateDivisionGoalCommandHandler CreateDivisionGoalHandler(
        TestDbContextLease lease, IOrganizationalScopeService scope)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            scope,
            CreateClosureGuard(lease));

    private static CreateDivisionKpiCommandHandler CreateDivisionKpiHandler(
        TestDbContextLease lease, IOrganizationalScopeService scope)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            scope,
            CreateClosureGuard(lease));

    private static GetDivisionPlanContextQueryHandler CreateContextHandler(
        TestDbContextLease lease, IOrganizationalScopeService scope)
        => new(
            new ScorecardRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new GoalRepository(lease.Context),
            new DivisionPeriodCalculationService(
                new DivisionGoalRepository(lease.Context),
                new FiscalYearRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                new KpiTargetRepository(lease.Context),
                new KpiActualValueRepository(lease.Context),
                new KpiScoreCalculator(new ScoreStrategyFactory()),
                new GoalScoreCalculator(),
                new ScorecardScoreCalculator(),
                TestBusinessClock.Real()),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new TestRagThresholdService(),
            scope);
}
