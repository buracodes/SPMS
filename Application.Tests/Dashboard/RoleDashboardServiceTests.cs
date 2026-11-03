using Application.Dashboard;
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
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Application.Tests.Dashboard;

public class RoleDashboardServiceTests
{

    /// <summary>Mirrors the permission set the seeder grants the executive role.</summary>
    private static readonly string[] ExecutivePermissions =
    {
        Permissions.DashboardView, Permissions.DashboardConfigure, Permissions.ReportsView, Permissions.ReportsExport,
        Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView, Permissions.KpiView,
        Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
        Permissions.DivisionView, Permissions.DivisionReportsView, Permissions.OrganizationView
    };

    /// <summary>Mirrors the permission set the seeder grants the data input role.</summary>
    private static readonly string[] DataInputPermissions =
    {
        Permissions.ScorecardView, Permissions.DashboardView, Permissions.GoalsView, Permissions.KraView, Permissions.KpiView,
        Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DataUpload, Permissions.DataValidate,
        Permissions.TargetsView, Permissions.ReportsView, Permissions.InitiativesView,
        Permissions.DivisionView, Permissions.DivisionActualsEnter, Permissions.DivisionReportsView,
        Permissions.OrganizationView
    };

    /// <summary>The view-only baseline every active HR employee receives.</summary>
    private static readonly string[] DivisionBaselinePermissions =
    {
        Permissions.DashboardView, Permissions.OrganizationView,
        Permissions.DivisionScorecardView, Permissions.DivisionPerformanceView,
        Permissions.DivisionInitiativeView
    };

    /// <summary>Mirrors the permission set the seeder grants the view only role.</summary>
    private static readonly string[] ViewOnlyPermissions =
    {
        Permissions.DashboardView, Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView,
        Permissions.KpiView, Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
        Permissions.DivisionView, Permissions.ReportsView, Permissions.OrganizationView
    };

    private static RoleDashboardService CreateService(
        TestDbContextLease lease,
        TestCurrentUserService user,
        TestEffectivePermissionService? effectivePermissions = null)
    {
        var context = lease.Context;

        // The dashboard resolves permissions live. Unless a test supplies its own live state
        // (to simulate a revocation), seed it from the claims the test declared so existing
        // composition tests keep exercising the same permission set.
        if (effectivePermissions is null)
        {
            effectivePermissions = new TestEffectivePermissionService();
            if (user.UserId is Guid liveUserId)
            {
                foreach (var permission in user.Permissions)
                {
                    effectivePermissions.Grant(liveUserId, permission);
                }
            }
        }
        var factory = new ScoreStrategyFactory();
        var clock = TestBusinessClock.Real();
        var scorePeriod = new ScorePeriodCalculationService(
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

        var divisionPeriod = new DivisionPeriodCalculationService(
            new DivisionGoalRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new KpiTargetRepository(context),
            new KpiActualValueRepository(context),
            new KpiScoreCalculator(factory),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            clock);

        return new RoleDashboardService(
            user,
            scorePeriod,
            new ScorecardRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new GoalRepository(context),
            new KpiRepository(context),
            new KpiActualValueRepository(context),
            new KpiTargetRepository(context),
            new InitiativeRepository(context),
            new DivisionPlanRepository(context),
            new DivisionScoreSnapshotRepository(context),
            divisionPeriod,
            new PeriodLockConfigRepository(context),
            new PeriodLockStatusService(
                new FiscalPeriodRepository(context),
                new PeriodLockConfigRepository(context),
                new SystemConfigRepository(context),
                new FiscalYearRepository(context),
                clock,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PeriodLockStatusService>.Instance),
            new AuditLogEntryRepository(context),
            new UserRepository(context),
            new GenericRepository<Role>(context),
            new InitiativeProgressCalculator(),
            clock,
            new RagThresholdService(
                new RagThresholdConfigRepository(context),
                new SpmsUnitOfWork(context)),
            effectivePermissions);
    }

    private static async Task<FiscalYear> SeedYearAsync(TestDbContextLease lease, int calendarYear = 2026)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(calendarYear);
        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));
        await lease.Context.SaveChangesAsync();
        return year;
    }

    private static async Task<Guid> EnsureUserAsync(TestDbContextLease lease, string? email = null)
    {
        var id = Guid.NewGuid();
        lease.Context.Users.Add(new User
        {
            Id = id,
            Email = email ?? $"{id:N}@spms.et",
            Username = email ?? $"{id:N}@spms.et",
            FullName = "KPI Owner",
            IsActive = true
        });

        await lease.Context.SaveChangesAsync();
        return id;
    }

    private static async Task<(Guid ScorecardId, Goal SecondGoal, Kpi Mine, Kpi SomeoneElse)> SeedHierarchyAsync(
        TestDbContextLease lease, FiscalYear year, Guid ownerId, Guid otherUserId)
    {
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "ECX Corporate Strategy",
            Scope = ScorecardScope.Corporate,
            ScorecardStatus = ScorecardStatus.Active
        };

        var firstGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 50 };
        var secondGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Serve Customers", Weight = 50 };
        var firstKra = new Kra { Id = Guid.NewGuid(), GoalId = firstGoal.Id, ScorecardId = scorecard.Id, Name = "Sales Growth", Weight = 100 };
        var secondKra = new Kra { Id = Guid.NewGuid(), GoalId = secondGoal.Id, ScorecardId = scorecard.Id, Name = "Service Quality", Weight = 100 };

        var mine = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = firstKra.Id,
            Name = "Revenue",
            Weight = 100,
            Unit = "ETB",
            IsActive = true,
            IsCumulative = true,
            OwnerId = ownerId,
            OptimizationType = KpiOptimizationType.Maximize,
            KpiType = KpiType.Lagging
        };

        var someoneElse = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = secondKra.Id,
            Name = "Complaint Resolution",
            Weight = 100,
            Unit = "%",
            IsActive = true,
            OwnerId = otherUserId,
            OptimizationType = KpiOptimizationType.Maximize,
            KpiType = KpiType.Lagging
        };

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.AddRange(firstGoal, secondGoal);
        lease.Context.Kras.AddRange(firstKra, secondKra);
        lease.Context.Kpis.AddRange(mine, someoneElse);
        await lease.Context.SaveChangesAsync();

        return (scorecard.Id, secondGoal, mine, someoneElse);
    }

    private static async Task<User> SeedUserAsync(TestDbContextLease lease, string email, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Username = email,
            FullName = fullName,
            IsActive = true
        };

        lease.Context.Users.Add(user);
        await lease.Context.SaveChangesAsync();
        return user;
    }

    /// <summary>Monthly targets for every fiscal month; actuals are 80% when requested.</summary>
    private static async Task SeedMonthlyTargetsAsync(
        TestDbContextLease lease, FiscalYear year, Guid kpiId, decimal monthlyTarget, bool withActuals)
    {
        var periods = await lease.Context.FiscalPeriods
            .Where(p => p.FiscalYearId == year.Id)
            .OrderBy(p => p.FiscalMonth)
            .ToListAsync();

        foreach (var period in periods)
        {
            lease.Context.KpiTargets.Add(new KpiTarget
            {
                KpiId = kpiId,
                FiscalPeriodId = period.Id,
                Granularity = TargetGranularity.Monthly,
                TargetValue = monthlyTarget
            });

            if (withActuals)
            {
                lease.Context.KpiActualValues.Add(new KpiActualValue
                {
                    KpiId = kpiId,
                    FiscalPeriodId = period.Id,
                    Value = monthlyTarget * 0.8m,
                    DataSource = KpiDataSource.Manual,
                    AsOfDate = period.EndDate
                });
            }
        }

        await lease.Context.SaveChangesAsync();
    }

    private static async Task<(Division Division, DivisionPlan Plan, DivisionGoal Goal, Kpi Kpi)> SeedDivisionPlanAsync(
        TestDbContextLease lease, Guid scorecardId, Guid parentGoalId, string divisionName = "Operations")
    {
        var division = new Division
        {
            Id = Guid.NewGuid(),
            Name = divisionName,
            Code = divisionName[..Math.Min(3, divisionName.Length)].ToUpperInvariant(),
            IsActive = true
        };

        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = division.Id,
            ScorecardId = scorecardId,
            PlanStatus = DivisionPlanStatus.Finalized
        };

        var goal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = parentGoalId,
            Name = divisionName + " delivery goal",
            Weight = 100m
        };

        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            DivisionGoalId = goal.Id,
            Name = divisionName + " throughput",
            Weight = 100m,
            Unit = "units",
            IsActive = true,
            OptimizationType = KpiOptimizationType.Maximize,
            KpiType = KpiType.Lagging
        };

        lease.Context.Divisions.Add(division);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(goal);
        lease.Context.Kpis.Add(kpi);
        await lease.Context.SaveChangesAsync();

        return (division, plan, goal, kpi);
    }

    [Fact]
    public async Task Target_vs_actual_groups_by_unit_using_identical_period_boundaries()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var revenueOwner = await SeedUserAsync(lease, "revenue@spms.et", "Revenue Tester");
        var serviceOwner = await SeedUserAsync(lease, "service@spms.et", "Service Tester");
        var (_, _, mine, someoneElse) = await SeedHierarchyAsync(lease, year, revenueOwner.Id, serviceOwner.Id);

        // "Revenue" is fully entered (80% of its ETB target); "Complaint Resolution" is
        // configured but has no actuals at all this year.
        await SeedMonthlyTargetsAsync(lease, year, mine.Id, monthlyTarget: 100m, withActuals: true);
        await SeedMonthlyTargetsAsync(lease, year, someoneElse.Id, monthlyTarget: 10m, withActuals: false);

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var widgets = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });

        var chart = widgets.TargetVsActual!;

        // Groups keep KPI order (revenue first), and each unit only sums its own values.
        // FR-605: "Revenue" is cumulative (ETB volume) so its year view sums the 12
        // monthly targets (and actuals); "%" KPIs aggregate by average, so its annual
        // target is the monthly value (10), and it has no actuals at all (null).
        Assert.Equal(new[] { "ETB", "%" }, chart.Categories);
        Assert.Equal(new decimal?[] { 1200m, 10m }, chart.Series[0].Values.ToArray());
        Assert.Equal(new decimal?[] { 960m, null }, chart.Series[1].Values.ToArray());

        // The actual series takes the configured RAG band: 80% is Amber under the FR-607
        // thresholds (95/70). A unit without any entered actuals is NoData, never Red.
        Assert.True(chart.Series[1].UseCategoryColors);
        Assert.Equal(DashboardRag.Amber, chart.CategoryColors[0]);
        Assert.Equal(DashboardRag.NoData, chart.CategoryColors[1]);

        Assert.Equal("2", chart.Cards.Single(c => c.Key == "targets").Value);
        Assert.Equal("1", chart.Cards.Single(c => c.Key == "actuals").Value);
        Assert.Equal("50% of targeted KPIs", chart.Cards.Single(c => c.Key == "actuals").Detail);
    }

    [Fact]
    public async Task Goal_performance_chart_ranks_goals_and_builds_drilldown_routes()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardId, secondGoal, mine, _) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        await SeedMonthlyTargetsAsync(lease, year, mine.Id, monthlyTarget: 100m, withActuals: true);

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var widgets = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });

        var chart = widgets.GoalPerformanceChart!;

        // The scored goal ranks first; the untouched goal sorts after it.
        Assert.Equal(new[] { "Grow Revenue", "Serve Customers" }, chart.Categories);
        Assert.Equal(2, chart.Series[0].Values.Count);
        Assert.All(chart.CategoryColors, color => Assert.Contains(
            color, new[] { "Green", "Amber", "Red", "NoData" }));

        Assert.Equal(
            $"/scorecards/{scorecardId}?goalId={secondGoal.Id}",
            chart.Routes[1]);
    }

    [Fact]
    public async Task Initiative_and_budget_charts_read_milestone_and_budget_data()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var ownerId = await EnsureUserAsync(lease);
        var now = DateTime.UtcNow;

        var active = new Initiative
        {
            Id = Guid.NewGuid(),
            Name = "Digital Platform",
            OwnerId = ownerId,
            Priority = InitiativePriority.High,
            Status = InitiativeStatus.InProgress,
            Budget = 150m,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            CreatedDate = now
        };
        var firstPeriod = await lease.Context.FiscalPeriods.FirstAsync(p => p.FiscalYearId == year.Id);
        active.Milestones.Add(new Milestone
        {
            Id = Guid.NewGuid(),
            Name = "Phase 1",
            DueDate = firstPeriod.StartDate,
            CompletionPercentage = 50,
            Weight = 100m,
        });

        var planned = new Initiative
        {
            Id = Guid.NewGuid(),
            Name = "Market Expansion",
            OwnerId = ownerId,
            Priority = InitiativePriority.Critical,
            Status = InitiativeStatus.NotStarted,
            Budget = 400m,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            CreatedDate = now
        };

        lease.Context.Initiatives.AddRange(active, planned);
        await lease.Context.SaveChangesAsync();

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var widgets = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });

        var delivery = widgets.InitiativeDeliveryChart!;
        Assert.Equal(new[] { "Market Expansion", "Digital Platform" }, delivery.Categories);
        Assert.Equal(new decimal?[] { 0m, 50m }, delivery.Series[0].Values.ToArray());
        Assert.Equal($"/initiatives/{planned.Id}", delivery.Routes[0]);

        var budget = widgets.BudgetAllocationChart!;
        Assert.Equal(new[] { "Low", "Medium", "High", "Critical" }, budget.Categories);
        Assert.Equal(new decimal?[] { null, null, 150m, 400m }, budget.Series[0].Values.ToArray());
        Assert.Equal("550", budget.Cards.Single(c => c.Key == "total").Value);
    }

    [Fact]
    public async Task Initiative_widgets_keep_milestones_when_the_year_has_no_scorecard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var ownerId = await EnsureUserAsync(lease);
        var now = DateTime.UtcNow;

        var active = new Initiative
        {
            Id = Guid.NewGuid(),
            Name = "Digital Platform",
            OwnerId = ownerId,
            Priority = InitiativePriority.High,
            Status = InitiativeStatus.InProgress,
            Budget = 150m,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            CreatedDate = now
        };
        var firstPeriod = await lease.Context.FiscalPeriods.FirstAsync(p => p.FiscalYearId == year.Id);
        active.Milestones.Add(new Milestone
        {
            Id = Guid.NewGuid(),
            Name = "Phase 1",
            DueDate = firstPeriod.StartDate,
            CompletionPercentage = 50,
            Weight = 100m,
        });

        lease.Context.Initiatives.Add(active);
        await lease.Context.SaveChangesAsync();

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var widgets = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });

        // The year has no scorecard, but initiative widgets must not degrade to milestone-less rows.
        var delivery = widgets.InitiativeDeliveryChart!;
        Assert.Equal(new[] { "Digital Platform" }, delivery.Categories);
        Assert.Equal(new decimal?[] { 50m }, delivery.Series[0].Values.ToArray());
        Assert.Equal("150", widgets.BudgetAllocationChart!.Cards.Single(c => c.Key == "total").Value);
    }

    [Fact]
    public async Task Executive_context_exposes_role_filter_and_quick_actions()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        await SeedHierarchyAsync(lease, year, await EnsureUserAsync(lease), await EnsureUserAsync(lease));

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            UserName = "executive",
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var context = await CreateService(lease, user).GetContextAsync(null);

        Assert.Equal(Roles.Executive, context.Role);
        Assert.Equal("OrganizationWide", context.DataScope);
        Assert.True(context.ReadOnly);
        Assert.False(string.IsNullOrWhiteSpace(context.Insight));
        Assert.Equal(year.Id, context.Filter.FiscalYearId);
        Assert.NotEmpty(context.Widgets);
        Assert.NotEmpty(context.QuickActions);
        Assert.All(context.QuickActions, action => Assert.False(string.IsNullOrWhiteSpace(action.Route)));

        // Filter options must be populated for the global filter controls.
        Assert.NotEmpty(context.FiscalYears);
        Assert.NotEmpty(context.Scorecards);
        Assert.NotEmpty(context.Periods);
    }

    [Fact]
    public async Task Data_input_user_only_sees_assigned_kpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "datainput@spms.et", "Data Tester");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other Tester");
        var (scorecardId, secondGoal, mine, someoneElse) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            UserName = "datainput@spms.et",
            Roles = new[] { Roles.DataInputUser },
            Permissions = DataInputPermissions
        };

        var service = CreateService(lease, user);

        var context = await service.GetContextAsync(null);
        Assert.Equal("AssignedOnly", context.DataScope);
        Assert.False(context.ReadOnly);
        Assert.Contains(context.Scorecards, option => option.Id == scorecardId);

        var widgets = await service.GetWidgetsAsync(null);
        var assigned = widgets.AssignedKpis!;
        Assert.Equal(new[] { mine.Id.ToString() }, assigned.Rows.Select(r => r.Id).ToArray());

        // The goal that only contains another user's KPI must not leak.
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetBudgetSummary);
        Assert.DoesNotContain(widgets.Widgets, w => w.Key == DashboardRoleCatalog.WidgetDivisionComparison);

        var drilldown = await service.GetGoalDrilldownAsync(secondGoal.Id, null);
        Assert.Equal(string.Empty, drilldown.GoalName);
        Assert.Empty(drilldown.Kras);
    }

    [Fact]
    public async Task Division_baseline_dashboard_reads_the_divisions_own_plan_only()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardId, corporateGoal, corporateKpi, corporateOtherKpi) =
            await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var (division, _, divisionGoal, divisionKpi) =
            await SeedDivisionPlanAsync(lease, scorecardId, corporateGoal.Id);
        await SeedMonthlyTargetsAsync(lease, year, divisionKpi.Id, monthlyTarget: 100m, withActuals: true);

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.ViewOnly },
            Permissions = DivisionBaselinePermissions,
            DivisionId = division.Id
        };

        var service = CreateService(lease, user);
        var context = await service.GetContextAsync(new() { PeriodType = "Year" });

        Assert.Equal("Division", context.DataScope);
        Assert.True(context.ReadOnly);
        Assert.Equal("DivisionView", context.Role);
        Assert.Contains(context.Scorecards, option => option.Id == scorecardId);
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetAssignedKpis);
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetDataEntryStatus);
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetPeriodLocks);
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetDivisionComparison);
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetGoalHealth);

        var widgets = await service.GetWidgetsAsync(new() { PeriodType = "Year" });

        Assert.Null(widgets.AssignedKpis);
        Assert.Null(widgets.DataEntryStatus);
        Assert.Null(widgets.PeriodLocks);
        Assert.Null(widgets.GoalHealth);

        Assert.Equal(80m, widgets.OverallScore!.Value);

        var goals = widgets.GoalPerformance!;
        var goalRow = Assert.Single(goals.Rows);
        Assert.Equal(divisionGoal.Name, goalRow.Cells["goal"]);
        Assert.Equal("/division-planning/" + scorecardId + "?divisionId=" + division.Id, goalRow.Route);

        var scorecard = Assert.Single(widgets.ScorecardPerformance!.Rows);
        Assert.Equal("/division-planning/" + scorecardId + "?divisionId=" + division.Id, scorecard.Route);

        // The division KPI's 80 score is Amber under the configured thresholds; the chart
        // colours the actual bar with that band rather than a hardcoded red/green rule.
        var comparison = widgets.TargetVsActual!;
        Assert.Equal(new[] { "units" }, comparison.Categories);
        Assert.True(comparison.Series[1].UseCategoryColors);
        Assert.Equal(DashboardRag.Amber, comparison.CategoryColors[0]);

        var trend = widgets.KpiPerformanceTrend!;
        var series = Assert.Single(trend.Series);
        Assert.Contains(divisionKpi.Name, series.Name);
        Assert.Contains(series.Values, value => value == 80m);

        // No corporate goal or KPI may leak into a division scoped payload.
        var serialized = System.Text.Json.JsonSerializer.Serialize(widgets);
        Assert.DoesNotContain(corporateGoal.Name, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(corporateKpi.Name, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(corporateOtherKpi.Name, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Division_baseline_opens_its_own_goal_drilldown_without_goals_view()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardId, corporateGoal, corporateKpi, corporateOtherKpi) =
            await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var (division, _, divisionGoal, divisionKpi) =
            await SeedDivisionPlanAsync(lease, scorecardId, corporateGoal.Id);
        await SeedMonthlyTargetsAsync(lease, year, divisionKpi.Id, monthlyTarget: 100m, withActuals: true);

        // The baseline employee holds no corporate goals:view right at all.
        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.ViewOnly },
            Permissions = DivisionBaselinePermissions,
            DivisionId = division.Id
        };

        var service = CreateService(lease, user);

        var widgets = await service.GetWidgetsAsync(new() { PeriodType = "Year" });
        var row = Assert.Single(widgets.GoalPerformance!.Rows);

        // The row carries the goal id so the shell can open the drawer for a division goal.
        Assert.Equal(divisionGoal.Id.ToString(), row.Id);

        var drilldown = await service.GetGoalDrilldownAsync(divisionGoal.Id, new() { PeriodType = "Year" });

        // Division Goal -> KPI directly, with no corporate KRA layer.
        Assert.True(drilldown.IsDivisionGoal);
        Assert.Equal(divisionGoal.Name, drilldown.GoalName);
        Assert.Empty(drilldown.Kras);

        var kpi = Assert.Single(drilldown.Kpis);
        Assert.Equal(divisionKpi.Name, kpi.Name);
        Assert.Equal(80m, kpi.Score);
        Assert.True(kpi.HasData);
        Assert.Equal(DashboardRag.Amber, kpi.RagLabel);

        var serialized = System.Text.Json.JsonSerializer.Serialize(drilldown);
        Assert.DoesNotContain(corporateGoal.Name, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(corporateKpi.Name, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(corporateOtherKpi.Name, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Division_drilldown_returns_empty_for_corporate_and_sibling_goal_ids()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardId, corporateGoal, _, _) =
            await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var (divisionA, _, goalA, _) =
            await SeedDivisionPlanAsync(lease, scorecardId, corporateGoal.Id, "Alpha");
        var (_, _, goalB, _) =
            await SeedDivisionPlanAsync(lease, scorecardId, corporateGoal.Id, "Beta");

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.ViewOnly },
            Permissions = DivisionBaselinePermissions,
            DivisionId = divisionA.Id
        };

        var service = CreateService(lease, user);

        // A corporate goal id and a sibling division's goal id must not resolve through the
        // division endpoint, even though both are reachable from other endpoints.
        foreach (var foreignGoalId in new[] { corporateGoal.Id, goalB.Id })
        {
            var foreign = await service.GetGoalDrilldownAsync(foreignGoalId, new() { PeriodType = "Year" });
            Assert.Equal(string.Empty, foreign.GoalName);
            Assert.False(foreign.IsDivisionGoal);
            Assert.Empty(foreign.Kras);
            Assert.Empty(foreign.Kpis);
        }

        var own = await service.GetGoalDrilldownAsync(goalA.Id, new() { PeriodType = "Year" });
        Assert.Equal(goalA.Name, own.GoalName);
        Assert.True(own.IsDivisionGoal);
        Assert.Single(own.Kpis);
    }

    [Fact]
    public async Task Division_drilldown_fails_closed_without_a_division_read_permission()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardId, corporateGoal, _, _) =
            await SeedHierarchyAsync(lease, year, owner.Id, other.Id);
        var (division, _, divisionGoal, _) =
            await SeedDivisionPlanAsync(lease, scorecardId, corporateGoal.Id);

        // Dashboard access plus a division claim is not enough: the caller still needs one
        // of the division read rights to open a division goal.
        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.ViewOnly },
            Permissions = new[] { Permissions.DashboardView, Permissions.OrganizationView },
            DivisionId = division.Id
        };

        var service = CreateService(lease, user);

        await Assert.ThrowsAsync<Application.Exceptions.UnauthorizedException>(
            () => service.GetGoalDrilldownAsync(divisionGoal.Id, new() { PeriodType = "Year" }));
    }

    [Fact]
    public async Task Corporate_goal_drilldown_keeps_the_kra_hierarchy()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (_, secondGoal, _, someoneElse) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var drilldown = await CreateService(lease, user)
            .GetGoalDrilldownAsync(secondGoal.Id, new() { PeriodType = "Year" });

        Assert.False(drilldown.IsDivisionGoal);
        Assert.Equal(secondGoal.Name, drilldown.GoalName);
        Assert.Empty(drilldown.Kpis);

        var kra = Assert.Single(drilldown.Kras);
        Assert.Equal("Service Quality", kra.Name);
        Assert.Equal(someoneElse.Name, Assert.Single(kra.Kpis).Name);
    }

    [Fact]
    public async Task Multi_division_dashboard_lists_and_opens_scorecards_from_each_authorized_division()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardA, corporateGoalA, _, _) =
            await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var (divisionA, _, goalA, kpiA) =
            await SeedDivisionPlanAsync(lease, scorecardA, corporateGoalA.Id, "Alpha");
        await SeedMonthlyTargetsAsync(lease, year, kpiA.Id, monthlyTarget: 100m, withActuals: true);

        // A second corporate scorecard planned only by the caller's secondary division.
        var scorecardB = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "ECX Secondary Strategy",
            Scope = ScorecardScope.Corporate,
            ScorecardStatus = ScorecardStatus.Active
        };
        var corporateGoalB = new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardB.Id,
            Name = "Secondary Corporate Goal",
            Weight = 100m
        };
        lease.Context.Scorecards.Add(scorecardB);
        lease.Context.Goals.Add(corporateGoalB);
        await lease.Context.SaveChangesAsync();

        var (divisionB, _, goalB, kpiB) =
            await SeedDivisionPlanAsync(lease, scorecardB.Id, corporateGoalB.Id, "Beta");
        await SeedMonthlyTargetsAsync(lease, year, kpiB.Id, monthlyTarget: 50m, withActuals: true);

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.ViewOnly },
            Permissions = DivisionBaselinePermissions,
            DivisionIds = new[] { divisionA.Id, divisionB.Id }
        };

        var service = CreateService(lease, user);
        var context = await service.GetContextAsync(new() { PeriodType = "Year" });

        Assert.Equal("Division", context.DataScope);
        Assert.Contains(context.Scorecards, option => option.Id == scorecardA);
        Assert.Contains(context.Scorecards, option => option.Id == scorecardB.Id);

        // The primary division backs the default scorecard, but requesting the secondary
        // division's scorecard must resolve that division's plan instead of returning empty.
        var widgets = await service.GetWidgetsAsync(new()
        {
            ScorecardId = scorecardB.Id,
            PeriodType = "Year"
        });

        var row = Assert.Single(widgets.GoalPerformance!.Rows);
        Assert.Equal(goalB.Name, row.Cells["goal"]);
        Assert.Equal(
            "/division-planning/" + scorecardB.Id + "?divisionId=" + divisionB.Id,
            row.Route);
        Assert.DoesNotContain(goalA.Name, System.Text.Json.JsonSerializer.Serialize(widgets), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Division_data_entry_right_adds_task_widgets_without_organization_visibility()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (scorecardId, corporateGoal, _, _) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var (division, _, divisionGoal, divisionKpi) =
            await SeedDivisionPlanAsync(lease, scorecardId, corporateGoal.Id);
        await SeedMonthlyTargetsAsync(lease, year, divisionKpi.Id, monthlyTarget: 100m, withActuals: false);

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.DataInputUser },
            Permissions = DivisionBaselinePermissions
                .Concat(new[] { Permissions.ActualsView, Permissions.DivisionActualsEnter })
                .ToArray(),
            DivisionId = division.Id
        };

        var service = CreateService(lease, user);
        var context = await service.GetContextAsync(null);
        var widgets = await service.GetWidgetsAsync(null);

        Assert.Equal("Division", context.DataScope);
        Assert.False(context.ReadOnly);
        Assert.Equal(Roles.DataInputUser, context.Role);

        Assert.Contains(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetAssignedKpis);
        Assert.Contains(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetDataEntryStatus);
        Assert.Contains(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetPeriodLocks);
        Assert.DoesNotContain(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetDivisionComparison);

        var tasks = widgets.AssignedKpis!;
        var row = Assert.Single(tasks.Rows);
        Assert.Equal(divisionGoal.Name, row.Cells["goal"]);
        Assert.Contains(tasks.Columns, column => column.Key == "deadline");
        Assert.Contains(tasks.Columns, column => column.Key == "action");

        var entryStatus = Assert.IsType<Application.DTOS.Dashboard.CompositeWidgetDto>(widgets.DataEntryStatus);
        Assert.NotEmpty(entryStatus.Rows);
        Assert.All(entryStatus.Columns, column => Assert.Contains(column.Key, entryStatus.Rows[0].Cells.Keys));

        Assert.NotNull(widgets.PeriodLocks);
    }

    [Fact]
    public async Task Kpi_performance_trend_tracks_the_highest_weight_kpis_in_scope()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (_, _, mine, someoneElse) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        await SeedMonthlyTargetsAsync(lease, year, mine.Id, monthlyTarget: 100m, withActuals: true);

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var widgets = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });
        var trend = widgets.KpiPerformanceTrend!;

        Assert.NotEmpty(trend.Categories);
        Assert.Equal(2, trend.Series.Count);
        Assert.Equal("%", trend.ValueSuffix);

        var revenue = trend.Series.Single(series => series.Name == mine.Name);
        Assert.Contains(revenue.Values, value => value == 80m);

        // A KPI with no actuals keeps its line but has no points, so it is not counted as Red.
        var noData = trend.Series.Single(series => series.Name == someoneElse.Name);
        Assert.All(noData.Values, value => Assert.Null(value));
    }

    [Fact]
    public async Task Data_input_user_with_no_assignments_gets_empty_state_not_other_people_data()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "datainput@spms.et", "Data Tester");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other Tester");
        var (_, _, _, someoneElse) = await SeedHierarchyAsync(lease, year, await EnsureUserAsync(lease), other.Id);

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.DataInputUser },
            Permissions = DataInputPermissions
        };

        var service = CreateService(lease, user);
        var context = await service.GetContextAsync(null);

        // No scorecard contains an assigned KPI, so the filter offers nothing.
        Assert.Empty(context.Scorecards);
        Assert.Null(context.Filter.ScorecardId);

        var widgets = await service.GetWidgetsAsync(null);
        Assert.Equal("0", widgets.Summary!.Cards.Single(c => c.Key == "assignedKpis").Value);
        Assert.Empty(widgets.AssignedKpis!.Rows);

        // No payload anywhere in the bundle may mention another user's KPI.
        var serialized = System.Text.Json.JsonSerializer.Serialize(widgets);
        Assert.DoesNotContain(someoneElse.Name, serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ytd", null, null, null)]
    [InlineData("Quarter", 1, null, null)]
    [InlineData("Month", null, 1, 1)]
    [InlineData("Year", null, null, null)]
    public async Task Period_filter_variants_are_accepted(
        string periodType, int? quarter, int? fromMonth, int? toMonth)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        await SeedHierarchyAsync(lease, year, await EnsureUserAsync(lease), await EnsureUserAsync(lease));

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var context = await CreateService(lease, user).GetContextAsync(new()
        {
            PeriodType = periodType,
            Quarter = quarter,
            FromMonth = fromMonth,
            ToMonth = toMonth
        });

        Assert.Equal(periodType, context.Filter.PeriodType);
        Assert.False(string.IsNullOrWhiteSpace(context.Filter.PeriodLabel));
    }

    [Fact]
    public async Task Unknown_scorecard_request_falls_back_to_a_scorecard_the_caller_may_see()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "datainput@spms.et", "Data Tester");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other Tester");
        var (scorecardId, _, _, _) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        var foreignScorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Unrelated Scorecard",
            Scope = ScorecardScope.Corporate
        };
        lease.Context.Scorecards.Add(foreignScorecard);
        await lease.Context.SaveChangesAsync();

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.DataInputUser },
            Permissions = DataInputPermissions
        };

        var context = await CreateService(lease, user).GetContextAsync(new() { ScorecardId = foreignScorecard.Id });

        // The caller cannot select a scorecard outside their scope.
        Assert.Equal(scorecardId, context.Filter.ScorecardId);
    }

    [Fact]
    public async Task Closed_fiscal_year_is_flagged_and_scores_stay_readable()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        year.FiscalYearStatus = FiscalYearStatus.Closed;
        await lease.Context.SaveChangesAsync();
        await SeedHierarchyAsync(lease, year, await EnsureUserAsync(lease), await EnsureUserAsync(lease));

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var context = await CreateService(lease, user).GetContextAsync(null);

        Assert.True(context.Filter.FiscalYearClosed);
        Assert.Contains(context.Widgets, w => w.Key == DashboardRoleCatalog.WidgetAlerts);
    }

    [Theory]
    [InlineData("Ytd", null, null, null, "Ytd")]
    [InlineData("Quarter", 2, null, null, "Quarter:2")]
    [InlineData("Month", null, 3, 3, "Month:3")]
    [InlineData("Year", null, null, null, "Year")]
    public async Task Period_options_mark_the_applied_period_as_selected(
        string periodType, int? quarter, int? fromMonth, int? toMonth, string expectedValue)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);

        // No scorecard at all: there is no engine snapshot, so the options have to fall
        // back to the requested period. Otherwise the selector would show nothing selected.
        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var context = await CreateService(lease, user).GetContextAsync(new()
        {
            PeriodType = periodType,
            Quarter = quarter,
            FromMonth = fromMonth,
            ToMonth = toMonth
        });

        Assert.Null(context.Filter.ScorecardId);
        Assert.Equal(expectedValue, Assert.Single(context.Periods, p => p.IsSelected).Value);
    }

    [Fact]
    public async Task Period_options_expose_every_month_of_a_year_that_is_already_closed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, calendarYear: 2001);
        year.FiscalYearStatus = FiscalYearStatus.Closed;
        await lease.Context.SaveChangesAsync();

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var context = await CreateService(lease, user).GetContextAsync(null);

        // A finished fiscal year must not be cut down to the months "so far".
        Assert.Equal(12, context.Periods.Count(p => p.Value.StartsWith("Month:", StringComparison.Ordinal)));
        Assert.All(context.Periods, p => Assert.Null(p.Detail));
    }

    /// <summary>
    /// FR-607: stored RagThresholdConfig rows must change how the dashboard bands the same score -
    /// widgets read the effective thresholds the request loaded, not constants. An 80% overall
    /// score is Amber under the FR-607 defaults (95/70) but flips to Red once an administrator
    /// stores a stricter configuration, proving the wiring end to end.
    /// </summary>
    [Fact]
    public async Task Widgets_band_scores_with_the_stored_thresholds_not_defaults()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (_, _, mine, _) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);

        await SeedMonthlyTargetsAsync(lease, year, mine.Id, monthlyTarget: 100m, withActuals: true);

        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var widgets = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });
        Assert.Equal(DashboardRag.Amber, widgets.OverallScore!.RagLabel);

        // An administrator stores a stricter Green/Amber split; the next dashboard load bands
        // the unchanged 80% score differently.
        lease.Context.RagThresholdConfigs.AddRange(
            new RagThresholdConfig
            {
                Id = Guid.NewGuid(),
                ScopeKey = RagThresholdDefaults.ScopeKey,
                Band = RagBand.Green,
                MinScore = 85m,
                Color = RagThresholdDefaults.GreenColor,
                Status = EntityStatus.Active,
                IsDeleted = false,
                CreatedDate = DateTime.UtcNow
            },
            new RagThresholdConfig
            {
                Id = Guid.NewGuid(),
                ScopeKey = RagThresholdDefaults.ScopeKey,
                Band = RagBand.Yellow,
                MinScore = 81m,
                MaxScore = 85m,
                Color = RagThresholdDefaults.YellowColor,
                Status = EntityStatus.Active,
                IsDeleted = false,
                CreatedDate = DateTime.UtcNow
            });
        await lease.Context.SaveChangesAsync();

        var restocked = await CreateService(lease, user).GetWidgetsAsync(new() { PeriodType = "Year" });
        Assert.Equal(DashboardRag.Red, restocked.OverallScore!.RagLabel);
    }

    [Fact]
    public async Task Live_revocation_overrides_stale_permission_claims_in_dashboard_composition()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (_, _, mine, _) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);
        await SeedMonthlyTargetsAsync(lease, year, mine.Id, monthlyTarget: 100m, withActuals: true);

        // The access token still carries the executive claims; only the live grant table matters.
        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var live = new TestEffectivePermissionService();
        foreach (var permission in ExecutivePermissions)
        {
            live.Grant(owner.Id, permission);
        }

        var granted = await CreateService(lease, user, live).GetContextAsync(new() { PeriodType = "Year" });
        Assert.Equal("OrganizationWide", granted.DataScope);
        Assert.Contains(granted.Widgets, w => w.Key == DashboardRoleCatalog.WidgetScorecardHealth);

        // Revoking a broad read right must recompose the dashboard on the very next request,
        // even though the stale JWT claim is still present.
        live.Revoke(owner.Id, Permissions.ScorecardView);

        var revokedService = CreateService(lease, user, live);
        var revokedContext = await revokedService.GetContextAsync(new() { PeriodType = "Year" });
        var revokedWidgets = await revokedService.GetWidgetsAsync(new() { PeriodType = "Year" });

        Assert.Equal("AssignedOnly", revokedContext.DataScope);
        Assert.NotEqual(Roles.Executive, revokedContext.Role);
        Assert.DoesNotContain(revokedContext.Widgets, w => w.Key == DashboardRoleCatalog.WidgetScorecardHealth);
        Assert.DoesNotContain(revokedContext.Widgets, w => w.Key == DashboardRoleCatalog.WidgetDivisionComparison);
        Assert.Null(revokedWidgets.ScorecardPerformance);
        Assert.Null(revokedWidgets.ScorecardHealth);
        Assert.Null(revokedWidgets.OverallScore);

        // Re-granting restores the same composition; nothing was cached from the revoked state.
        live.Grant(owner.Id, Permissions.ScorecardView);
        var restored = await CreateService(lease, user, live).GetContextAsync(new() { PeriodType = "Year" });
        Assert.Equal("OrganizationWide", restored.DataScope);
    }

    [Fact]
    public async Task Revoked_goals_view_claim_cannot_open_the_corporate_goal_drilldown()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var owner = await SeedUserAsync(lease, "owner@spms.et", "Owner");
        var other = await SeedUserAsync(lease, "other@spms.et", "Other");
        var (_, goal, mine, _) = await SeedHierarchyAsync(lease, year, owner.Id, other.Id);
        await SeedMonthlyTargetsAsync(lease, year, mine.Id, monthlyTarget: 100m, withActuals: true);

        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            Roles = new[] { Roles.Executive },
            Permissions = ExecutivePermissions
        };

        var live = new TestEffectivePermissionService();
        foreach (var permission in ExecutivePermissions.Where(p => p != Permissions.GoalsView))
        {
            live.Grant(owner.Id, permission);
        }

        var service = CreateService(lease, user, live);

        // The token says goals:view but the live grant table has revoked it, so the protected
        // drill-down endpoint must fail closed exactly as it would for a fresh token.
        await Assert.ThrowsAsync<Application.Exceptions.UnauthorizedException>(
            () => service.GetGoalDrilldownAsync(goal.Id, null));

        live.Grant(owner.Id, Permissions.GoalsView);
        var drilldown = await CreateService(lease, user, live).GetGoalDrilldownAsync(goal.Id, null);
        Assert.Equal(goal.Name, drilldown.GoalName);
    }
}
