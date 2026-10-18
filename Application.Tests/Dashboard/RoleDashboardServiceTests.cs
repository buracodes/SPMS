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
    private const string Password = "P@ssw0rd!";

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

    /// <summary>Mirrors the permission set the seeder grants the view only role.</summary>
    private static readonly string[] ViewOnlyPermissions =
    {
        Permissions.DashboardView, Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView,
        Permissions.KpiView, Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
        Permissions.DivisionView, Permissions.ReportsView, Permissions.OrganizationView
    };

    private static RoleDashboardService CreateService(TestDbContextLease lease, TestCurrentUserService user)
    {
        var context = lease.Context;
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
                new SpmsUnitOfWork(context)));
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
            PasswordHash = Password,
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
            PasswordHash = Password,
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

        // A unit without any entered actuals is grey, never a misleading zero bar.
        Assert.Equal("slate", chart.CategoryColors[1]);

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
}
