using Application.Dashboard;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using System;

namespace Application.Tests.Dashboard;

public class DashboardRoleCatalogTests
{
    [Fact]
    public void Every_role_has_a_profile()
    {
        Assert.Equal(5, DashboardRoleCatalog.Profiles.Count);
        Assert.True(DashboardRoleCatalog.Profiles.ContainsKey(Roles.Admin));
        Assert.True(DashboardRoleCatalog.Profiles.ContainsKey(Roles.PowerUser));
        Assert.True(DashboardRoleCatalog.Profiles.ContainsKey(Roles.Executive));
        Assert.True(DashboardRoleCatalog.Profiles.ContainsKey(Roles.DataInputUser));
        Assert.True(DashboardRoleCatalog.Profiles.ContainsKey(Roles.ViewOnly));
    }

    [Fact]
    public void Only_data_input_user_is_limited_to_assigned_records()
    {
        Assert.Equal(
            DashboardDataScope.AssignedOnly,
            DashboardRoleCatalog.Profiles[Roles.DataInputUser].DataScope);

        Assert.All(
            DashboardRoleCatalog.Profiles.Where(p => p.Key != Roles.DataInputUser),
            profile => Assert.Equal(DashboardDataScope.OrganizationWide, profile.Value.DataScope));
    }

    [Fact]
    public void Executive_and_view_only_are_read_only()
    {
        Assert.True(DashboardRoleCatalog.Profiles[Roles.Executive].IsReadOnly);
        Assert.True(DashboardRoleCatalog.Profiles[Roles.ViewOnly].IsReadOnly);

        Assert.False(DashboardRoleCatalog.Profiles[Roles.Admin].IsReadOnly);
        Assert.False(DashboardRoleCatalog.Profiles[Roles.PowerUser].IsReadOnly);
        Assert.False(DashboardRoleCatalog.Profiles[Roles.DataInputUser].IsReadOnly);
    }

    [Fact]
    public void Resolve_profile_prefers_the_most_privileged_role()
    {
        var profile = DashboardRoleCatalog.Resolve(
            new[] { Roles.ViewOnly, Roles.Executive, Roles.PowerUser });

        Assert.Equal(Roles.PowerUser, profile.Role);
    }

    [Fact]
    public void Resolve_profile_falls_back_to_view_only()
    {
        var profile = DashboardRoleCatalog.Resolve(Array.Empty<string>());

        Assert.Equal(Roles.ViewOnly, profile.Role);
    }

    [Fact]
    public void Data_input_user_never_receives_financial_or_administrative_widgets()
    {
        var profile = DashboardRoleCatalog.Profiles[Roles.DataInputUser];

        Assert.Contains(DashboardRoleCatalog.WidgetAssignedKpis, profile.Widgets);
        Assert.Contains(DashboardRoleCatalog.WidgetDataEntryStatus, profile.Widgets);
        Assert.Contains(DashboardRoleCatalog.WidgetPeriodLocks, profile.Widgets);

        Assert.DoesNotContain(DashboardRoleCatalog.WidgetBudgetSummary, profile.Widgets);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, profile.Widgets);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetSystemStats, profile.Widgets);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetAuditActivity, profile.Widgets);
    }

    [Fact]
    public void Widget_registry_drops_widgets_the_role_may_not_see()
    {
        var profile = DashboardRoleCatalog.Profiles[Roles.ViewOnly];

        var keys = DashboardRoleCatalog
            .ResolveWidgets(profile, new[] { Permissions.ScorecardView })
            .Select(widget => widget.Key)
            .ToList();

        Assert.DoesNotContain(DashboardRoleCatalog.WidgetPeriodLocks, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetOverallScore, keys);
    }

    [Fact]
    public void Quick_actions_are_filtered_by_permission()
    {
        var profile = DashboardRoleCatalog.Profiles[Roles.ViewOnly];

        var actions = DashboardRoleCatalog.ResolveActions(profile, new[] { Permissions.ScorecardView });

        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.Equal(Permissions.ScorecardView, action.RequiredPermission));
    }

    [Fact]
    public void Rag_thresholds_match_the_frd()
    {
        Assert.Equal("Green", DashboardRag.FromScore(100m));
        Assert.Equal("Green", DashboardRag.FromScore(95m));
        Assert.Equal("Amber", DashboardRag.FromScore(94.9m));
        Assert.Equal("Amber", DashboardRag.FromScore(70m));
        Assert.Equal("Red", DashboardRag.FromScore(69.9m));
        Assert.Equal("Red", DashboardRag.FromScore(0m));
        Assert.Equal("NoData", DashboardRag.FromScore(null));

        Assert.True(DashboardRag.IsRed(50m));
        Assert.False(DashboardRag.IsRed(null));
    }

    /// <summary>
    /// Every role must land on the same page with a usable dashboard, using the
    /// permissions the seeder actually grants. This is the guard against a role
    /// silently losing every widget because of a permission/widget mismatch.
    /// </summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.PowerUser)]
    [InlineData(Roles.Executive)]
    [InlineData(Roles.DataInputUser)]
    [InlineData(Roles.ViewOnly)]
    public void Every_seeded_role_receives_a_complete_dashboard(string role)
    {
        var profile = DashboardRoleCatalog.Profiles[role];
        var permissions = SeededPermissionsFor(role);

        var widgets = DashboardRoleCatalog.ResolveWidgets(profile, permissions);
        var actions = DashboardRoleCatalog.ResolveActions(profile, permissions);

        Assert.NotEmpty(widgets);
        Assert.NotEmpty(actions);
        Assert.Contains(DashboardRoleCatalog.WidgetKpiSummary, widgets.Select(w => w.Key));
        Assert.All(widgets, widget => Assert.All(
            widget.RequiredPermission is null ? Array.Empty<string>() : new[] { widget.RequiredPermission },
            required => Assert.Contains(required, permissions)));
        Assert.All(actions, action => Assert.All(
            action.RequiredPermission is null ? Array.Empty<string>() : new[] { action.RequiredPermission },
            required => Assert.Contains(required, permissions)));
    }

    [Fact]
    public void Read_only_roles_never_receive_administration_widgets()
    {
        foreach (var role in new[] { Roles.Executive, Roles.ViewOnly })
        {
            var permissions = SeededPermissionsFor(role);
            var keys = DashboardRoleCatalog
                .ResolveWidgets(DashboardRoleCatalog.Profiles[role], permissions)
                .Select(widget => widget.Key)
                .ToList();

            Assert.DoesNotContain(DashboardRoleCatalog.WidgetSystemStats, keys);
            Assert.DoesNotContain(DashboardRoleCatalog.WidgetAuditActivity, keys);
            Assert.DoesNotContain(DashboardRoleCatalog.WidgetDataEntryStatus, keys);
            Assert.DoesNotContain(DashboardRoleCatalog.WidgetPeriodLocks, keys);
        }
    }

    [Fact]
    public void Data_input_user_with_seeded_permissions_only_sees_assigned_work()
    {
        var permissions = SeededPermissionsFor(Roles.DataInputUser);

        var keys = DashboardRoleCatalog
            .ResolveWidgets(DashboardRoleCatalog.Profiles[Roles.DataInputUser], permissions)
            .Select(widget => widget.Key)
            .ToList();

        Assert.Contains(DashboardRoleCatalog.WidgetAssignedKpis, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetDataEntryStatus, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetPeriodLocks, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetSystemStats, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetAuditActivity, keys);
    }

    [Fact]
    public void Every_role_gets_a_target_vs_actual_widget()
    {
        foreach (var role in new[] { Roles.Admin, Roles.PowerUser, Roles.Executive, Roles.DataInputUser, Roles.ViewOnly })
        {
            Assert.Contains(DashboardRoleCatalog.WidgetTargetVsActual, DashboardRoleCatalog.Profiles[role].Widgets);
        }
    }

    [Fact]
    public void Data_input_user_never_receives_peer_analytics_charts()
    {
        var profile = DashboardRoleCatalog.Profiles[Roles.DataInputUser];

        Assert.DoesNotContain(DashboardRoleCatalog.WidgetGoalPerformanceChart, profile.Widgets);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionPerformanceChart, profile.Widgets);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetBudgetAllocationChart, profile.Widgets);
    }

    [Fact]
    public void Division_and_budget_charts_only_reach_organization_wide_roles()
    {
        foreach (var role in new[] { Roles.Admin, Roles.PowerUser, Roles.Executive, Roles.ViewOnly })
        {
            var profile = DashboardRoleCatalog.Profiles[role];

            Assert.Equal(DashboardDataScope.OrganizationWide, profile.DataScope);
            Assert.Contains(DashboardRoleCatalog.WidgetDivisionPerformanceChart, profile.Widgets);

            if (role != Roles.ViewOnly)
            {
                Assert.Contains(DashboardRoleCatalog.WidgetBudgetAllocationChart, profile.Widgets);
            }
        }
    }

    [Fact]
    public void Executive_and_view_only_quick_action_routes_target_real_pages()
    {
        foreach (var role in new[] { Roles.Executive, Roles.ViewOnly })
        {
            var performance = DashboardRoleCatalog
                .Profiles[role]
                .QuickActions
                .Single(action => action.Key == "performance");

            // Older versions pointed at /scorecardss/targets/performance, which has no route.
            Assert.Equal("/scorecardss/performance", performance.Route);
        }
    }

    /// <summary>Mirrors the role to permission map applied by the database seeder.</summary>
    private static string[] SeededPermissionsFor(string role) => role switch
    {
        Roles.Admin => Permissions.All.Select(p => p.Name).ToArray(),
        Roles.PowerUser => Permissions.All
            .Select(p => p.Name)
            .Except(new[] { Permissions.AdminUsersManage, Permissions.AdminRolesManage, Permissions.AdminConfigManage })
            .ToArray(),
        Roles.Executive => new[]
        {
            Permissions.DashboardView, Permissions.DashboardConfigure, Permissions.ReportsView, Permissions.ReportsExport,
            Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView, Permissions.KpiView,
            Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
            Permissions.DivisionView, Permissions.DivisionReportsView, Permissions.OrganizationView
        },
        Roles.DataInputUser => new[]
        {
            Permissions.ScorecardView, Permissions.DashboardView, Permissions.GoalsView, Permissions.KraView, Permissions.KpiView,
            Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DataUpload, Permissions.DataValidate,
            Permissions.TargetsView, Permissions.ReportsView, Permissions.InitiativesView,
            Permissions.DivisionView, Permissions.DivisionActualsEnter, Permissions.DivisionReportsView,
            Permissions.OrganizationView
        },
        Roles.ViewOnly => new[]
        {
            Permissions.DashboardView, Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView,
            Permissions.KpiView, Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
            Permissions.DivisionView, Permissions.ReportsView, Permissions.OrganizationView
        },
        _ => Array.Empty<string>()
    };
}
