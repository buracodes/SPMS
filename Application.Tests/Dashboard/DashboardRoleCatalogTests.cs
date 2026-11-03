using Application.Dashboard;
using Domain.Constants;
using System;

namespace Application.Tests.Dashboard;

/// <summary>
/// The dashboard is composed from effective permissions plus the HR division claim, never
/// from a role name. These tests lock that contract: the union of several rights is honoured,
/// a division claim always narrows the data set, and an entry right alone never becomes a
/// read-all right.
/// </summary>
public class DashboardRoleCatalogTests
{
    private static readonly string[] DivisionBaseline =
    {
        Permissions.DashboardView,
        Permissions.OrganizationView,
        Permissions.DivisionScorecardView,
        Permissions.DivisionPerformanceView,
        Permissions.DivisionInitiativeView
    };

    private static readonly Guid DivisionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string[] Keys(DashboardRoleProfile profile)
        => profile.Widgets.Select(widget => widget.Key).ToArray();

    [Fact]
    public void No_permissions_resolves_to_a_read_only_assigned_view()
    {
        var profile = DashboardRoleCatalog.Resolve(Array.Empty<string>(), null);

        Assert.Equal(DashboardDataScope.AssignedOnly, profile.DataScope);
        Assert.True(profile.IsReadOnly);
        Assert.Equal(Roles.ViewOnly, profile.Role);
        Assert.Contains(DashboardRoleCatalog.WidgetKpiSummary, Keys(profile));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetAssignedKpis, Keys(profile));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDataEntryStatus, Keys(profile));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, Keys(profile));
    }

    [Fact]
    public void Division_baseline_is_scoped_to_the_division_and_stays_read_only()
    {
        var profile = DashboardRoleCatalog.Resolve(DivisionBaseline, DivisionId);
        var keys = Keys(profile);

        Assert.Equal(DashboardDataScope.Division, profile.DataScope);
        Assert.True(profile.IsReadOnly);

        // Everything the default employee is promised for their own division.
        Assert.Contains(DashboardRoleCatalog.WidgetOverallScore, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetRagDistribution, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetScorecardPerformance, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetGoalPerformance, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetGoalPerformanceChart, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetTargetVsActual, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetKpiPerformanceTrend, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetPerformanceTrend, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetInitiativeStatus, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetMilestoneTrend, keys);

        // Cross-division and task widgets must not leak into the read-only view.
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionPerformanceChart, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetScorecardHealth, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetGoalHealth, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetTopBottomKpis, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetKpiExceptions, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetBudgetSummary, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetAssignedKpis, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDataEntryStatus, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetPeriodLocks, keys);
    }

    [Fact]
    public void Division_employee_with_entry_right_gets_the_task_widgets_but_not_corporate_reads()
    {
        var permissions = DivisionBaseline.Concat(new[]
        {
            Permissions.ActualsView,
            Permissions.DivisionActualsEnter
        }).ToArray();

        var profile = DashboardRoleCatalog.Resolve(permissions, DivisionId);
        var keys = Keys(profile);

        Assert.Equal(DashboardDataScope.Division, profile.DataScope);
        Assert.False(profile.IsReadOnly);
        Assert.Equal(Roles.DataInputUser, profile.Role);

        Assert.Contains(DashboardRoleCatalog.WidgetAssignedKpis, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetDataEntryStatus, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetPeriodLocks, keys);

        // An entry right inside one division still grants no cross-division view.
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetSystemStats, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetAuditActivity, keys);
    }

    [Fact]
    public void Entry_right_without_a_division_or_broad_read_stays_assigned_only()
    {
        var profile = DashboardRoleCatalog.Resolve(
            new[] { Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DashboardView },
            null);

        var keys = Keys(profile);

        Assert.Equal(DashboardDataScope.AssignedOnly, profile.DataScope);
        Assert.Contains(DashboardRoleCatalog.WidgetAssignedKpis, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetDataEntryStatus, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetPeriodLocks, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetScorecardPerformance, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, keys);
    }

    [Fact]
    public void Multiple_groups_union_their_rights()
    {
        // A user in a performance group (scorecards + monitoring), an initiative group and a
        // report group receives the union, not the rights of one "most privileged" role.
        var permissions = new[]
        {
            Permissions.DashboardView,
            Permissions.ScorecardView,
            Permissions.PerformanceMonitorView,
            Permissions.InitiativesView,
            Permissions.ReportsView
        };

        var profile = DashboardRoleCatalog.Resolve(permissions, null);
        var keys = Keys(profile);

        Assert.Equal(DashboardDataScope.OrganizationWide, profile.DataScope);
        Assert.Contains(DashboardRoleCatalog.WidgetScorecardPerformance, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetDivisionComparison, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetInitiativeStatus, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetReportsSummary, keys);
    }

    [Fact]
    public void Division_claim_narrows_an_otherwise_corporate_account()
    {
        var corporate = new[]
        {
            Permissions.DashboardView,
            Permissions.ScorecardView,
            Permissions.GoalsView,
            Permissions.KpiView,
            Permissions.PerformanceMonitorView
        };

        var profile = DashboardRoleCatalog.Resolve(corporate, DivisionId);
        var keys = Keys(profile);

        Assert.Equal(DashboardDataScope.Division, profile.DataScope);

        // The corporate read rights still add their widgets, but the cross-division ones are
        // dropped because the HR division claim is authoritative.
        Assert.Contains(DashboardRoleCatalog.WidgetGoalPerformance, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionPerformanceChart, keys);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetScorecardHealth, keys);
    }

    [Fact]
    public void Read_only_is_derived_from_write_rights_not_from_a_role_name()
    {
        Assert.True(DashboardRoleCatalog.Resolve(
            new[] { Permissions.ScorecardView, Permissions.ActualsView, Permissions.ReportsView }, null).IsReadOnly);

        Assert.False(DashboardRoleCatalog.Resolve(
            new[] { Permissions.ScorecardView, Permissions.ActualsEnter }, null).IsReadOnly);

        Assert.False(DashboardRoleCatalog.Resolve(
            new[] { Permissions.DivisionScorecardView, Permissions.DivisionActualsEnter }, DivisionId).IsReadOnly);
    }

    [Fact]
    public void Admin_rights_produce_the_administration_widgets()
    {
        var profile = DashboardRoleCatalog.Resolve(
            new[]
            {
                Permissions.DashboardView,
                Permissions.ScorecardView,
                Permissions.KpiView,
                Permissions.AdminConfigManage,
                Permissions.AdminAuditView,
                Permissions.AdminUsersManage
            },
            null);

        var keys = Keys(profile);

        Assert.Equal(Roles.Admin, profile.Role);
        Assert.Contains(DashboardRoleCatalog.WidgetSystemStats, keys);
        Assert.Contains(DashboardRoleCatalog.WidgetAuditActivity, keys);
    }

    [Fact]
    public void Executive_read_only_composition_has_no_configuration_widgets()
    {
        var profile = DashboardRoleCatalog.Resolve(
            new[]
            {
                Permissions.DashboardView,
                Permissions.ScorecardView,
                Permissions.GoalsView,
                Permissions.KpiView,
                Permissions.ReportsView,
                Permissions.ReportsExport,
                Permissions.PerformanceMonitorView
            },
            null,
            new[] { Roles.Executive });

        Assert.Equal(Roles.Executive, profile.Role);
        Assert.True(profile.IsReadOnly);
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetSystemStats, Keys(profile));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetAuditActivity, Keys(profile));
    }

    [Fact]
    public void Explicit_executive_role_changes_only_the_display_persona()
    {
        var permissions = new[]
        {
            Permissions.DashboardView,
            Permissions.ScorecardView,
            Permissions.PerformanceMonitorView,
            Permissions.ReportsView
        };

        var withoutLabel = DashboardRoleCatalog.Resolve(permissions, null);
        var withLabel = DashboardRoleCatalog.Resolve(permissions, null, new[] { Roles.Executive });

        // The same live rights must always produce the same authorization state; the
        // executive label may only change the display text.
        Assert.Equal(withoutLabel.DataScope, withLabel.DataScope);
        Assert.Equal(withoutLabel.IsReadOnly, withLabel.IsReadOnly);
        Assert.Equal(Keys(withoutLabel), Keys(withLabel));
        Assert.Equal(
            withoutLabel.QuickActions.Select(action => action.Key),
            withLabel.QuickActions.Select(action => action.Key));

        Assert.Equal(Roles.PowerUser, withoutLabel.Role);
        Assert.Equal(Roles.Executive, withLabel.Role);
    }

    [Fact]
    public void Reporting_and_configuration_rights_do_not_infer_the_executive_persona()
    {
        // A read-only reporting analyst with export and dashboard configuration rights is
        // still a Power User: those rights do not make the account an executive.
        var permissions = new[]
        {
            Permissions.DashboardView,
            Permissions.ScorecardView,
            Permissions.PerformanceMonitorView,
            Permissions.ReportsView,
            Permissions.ReportsExport,
            Permissions.DashboardConfigure
        };

        var profile = DashboardRoleCatalog.Resolve(permissions, null, new[] { Roles.PowerUser });

        Assert.Equal(Roles.PowerUser, profile.Role);
        Assert.NotEqual("Executive overview", profile.DisplayName);
        Assert.Equal(DashboardDataScope.OrganizationWide, profile.DataScope);
    }

    [Fact]
    public void Executive_label_cannot_grant_scope_or_widgets()
    {
        var withoutLabel = DashboardRoleCatalog.Resolve(Array.Empty<string>(), null);
        var withLabel = DashboardRoleCatalog.Resolve(Array.Empty<string>(), null, new[] { Roles.Executive });

        // An explicit persona is display only: with no read right the caller stays
        // assigned-only, exactly as before the label was applied.
        Assert.Equal(DashboardDataScope.AssignedOnly, withLabel.DataScope);
        Assert.Equal(withoutLabel.DataScope, withLabel.DataScope);
        Assert.Equal(Keys(withoutLabel), Keys(withLabel));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetDivisionComparison, Keys(withLabel));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetScorecardHealth, Keys(withLabel));
        Assert.DoesNotContain(DashboardRoleCatalog.WidgetSystemStats, Keys(withLabel));
    }

    [Fact]
    public void Every_widget_and_action_respects_its_any_of_permission_gate()
    {
        var permissionSets = new[]
        {
            Array.Empty<string>(),
            DivisionBaseline,
            DivisionBaseline.Concat(new[] { Permissions.DivisionActualsEnter }).ToArray(),
            Permissions.All.Select(p => p.Name).ToArray()
        };

        foreach (var permissions in permissionSets)
        {
            var granted = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            var profile = DashboardRoleCatalog.Resolve(permissions, DivisionId);

            Assert.NotEmpty(profile.Widgets);
            Assert.All(profile.Widgets, widget =>
            {
                Assert.True(DashboardRoleCatalog.Widgets.ContainsKey(widget.Key));
                Assert.True(
                    widget.RequiresAnyPermission.Count == 0 || widget.RequiresAnyPermission.Any(granted.Contains),
                    $"{widget.Key} was resolved without any of its required permissions.");
            });

            Assert.All(profile.QuickActions, action =>
                Assert.True(
                    action.RequiredPermission is null || granted.Contains(action.RequiredPermission),
                    $"{action.Key} was resolved without {action.RequiredPermission}."));
        }
    }

    [Fact]
    public void Quick_actions_route_to_pages_the_caller_can_open()
    {
        var divisionRead = DashboardRoleCatalog
            .Resolve(DivisionBaseline, DivisionId)
            .QuickActions
            .Single(action => action.Key == "performance");
        Assert.Equal("/division-planning", divisionRead.Route);

        var corporateRead = DashboardRoleCatalog
            .Resolve(new[] { Permissions.ScorecardView }, null)
            .QuickActions
            .Single(action => action.Key == "performance");
        Assert.Equal("/scorecardss/performance", corporateRead.Route);

        var divisionEntry = DashboardRoleCatalog
            .Resolve(DivisionBaseline.Append(Permissions.DivisionActualsEnter).ToArray(), DivisionId)
            .QuickActions
            .Single(action => action.Key == "entry");
        Assert.Equal("/division-planning", divisionEntry.Route);

        var corporateEntry = DashboardRoleCatalog
            .Resolve(new[] { Permissions.ActualsEnter }, null)
            .QuickActions
            .Single(action => action.Key == "entry");
        Assert.Equal("/data-entry", corporateEntry.Route);
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
}
