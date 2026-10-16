using Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Dashboard;

/// <summary>How far a role may see into the strategy data set.</summary>
public enum DashboardDataScope
{
    /// <summary>Every scorecard, goal, KPI and initiative the role can see by permission.</summary>
    OrganizationWide = 1,

    /// <summary>Only records assigned to the signed-in user (their KPIs, initiatives, divisions).</summary>
    AssignedOnly = 2
}

/// <summary>A single dashboard widget the shell can render.</summary>
public sealed class DashboardWidgetDefinition
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Grid width: 1 = third, 2 = two thirds, 3 = full width.</summary>
    public int Span { get; init; } = 2;

    /// <summary>Permission required to receive this widget's payload. Null = any dashboard viewer.</summary>
    public string? RequiredPermission { get; init; }
}

/// <summary>A navigation action offered to a role, permission checked server side.</summary>
public sealed class DashboardQuickActionDefinition
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Route { get; init; }

    public required string Icon { get; init; }

    public string? RequiredPermission { get; init; }

    public string? Emphasis { get; init; }
}

/// <summary>The resolved dashboard configuration for one role.</summary>
public sealed class DashboardRoleProfile
{
    public required string Role { get; init; }

    public required string DisplayName { get; init; }

    public required string Purpose { get; init; }

    public required string Insight { get; init; }

    public required DashboardDataScope DataScope { get; init; }

    /// <summary>True when the role may never edit strategy data (Executive, View Only).</summary>
    public bool IsReadOnly { get; init; }

    public required IReadOnlyList<string> Widgets { get; init; }

    public required IReadOnlyList<DashboardQuickActionDefinition> QuickActions { get; init; }
}

/// <summary>
/// Server-side dashboard configuration. The dashboard shell is shared; the role decides
/// which widgets, actions and data scope are rendered, so no role needs its own page.
/// </summary>
public static class DashboardRoleCatalog
{
    public const string WidgetKpiSummary = "kpiSummary";
    public const string WidgetOverallScore = "overallScore";
    public const string WidgetRagDistribution = "ragDistribution";
    public const string WidgetPerformanceTrend = "performanceTrend";
    public const string WidgetScorecardPerformance = "scorecardPerformance";
    public const string WidgetScorecardHealth = "scorecardHealth";
    public const string WidgetGoalHealth = "goalHealth";
    public const string WidgetGoalPerformance = "goalPerformance";
    public const string WidgetGoalPerformanceChart = "goalPerformanceChart";
    public const string WidgetTopBottomKpis = "topBottomKpis";
    public const string WidgetKpiExceptions = "kpiExceptions";
    public const string WidgetStrategicAlignment = "strategicAlignment";
    public const string WidgetInitiativeStatus = "initiativeStatus";
    public const string WidgetInitiativeDeliveryChart = "initiativeDeliveryChart";
    public const string WidgetMilestoneTrend = "milestoneTrend";
    public const string WidgetBudgetSummary = "budgetSummary";
    public const string WidgetBudgetAllocationChart = "budgetAllocationChart";
    public const string WidgetDivisionComparison = "divisionComparison";
    public const string WidgetDivisionPerformanceChart = "divisionPerformanceChart";
    public const string WidgetTargetVsActual = "targetVsActual";
    public const string WidgetAssignedKpis = "assignedKpis";
    public const string WidgetDataEntryStatus = "dataEntryStatus";
    public const string WidgetPeriodLocks = "periodLocks";
    public const string WidgetSystemStats = "systemStats";
    public const string WidgetAuditActivity = "auditActivity";
    public const string WidgetReportsSummary = "reportsSummary";
    public const string WidgetAlerts = "alerts";

    public static readonly IReadOnlyDictionary<string, DashboardWidgetDefinition> Widgets =
        new Dictionary<string, DashboardWidgetDefinition>(StringComparer.Ordinal)
        {
            [WidgetKpiSummary] = new()
            {
                Key = WidgetKpiSummary,
                Title = "Performance summary",
                Description = "Headline performance for the selected fiscal year, scorecard and period.",
                Span = 3
            },
            [WidgetOverallScore] = new()
            {
                Key = WidgetOverallScore,
                Title = "Overall score",
                Description = "Weighted scorecard score produced by the SPMS calculation engine.",
                Span = 1
            },
            [WidgetRagDistribution] = new()
            {
                Key = WidgetRagDistribution,
                Title = "KPI RAG distribution",
                Description = "How many KPIs are on track, at risk, off track or still without data.",
                Span = 1
            },
            [WidgetPerformanceTrend] = new()
            {
                Key = WidgetPerformanceTrend,
                Title = "Performance trend",
                Description = "Month-by-month score movement across the fiscal year.",
                Span = 2
            },
            [WidgetScorecardPerformance] = new()
            {
                Key = WidgetScorecardPerformance,
                Title = "Scorecard performance",
                Description = "Every scorecard the role may see, with score, RAG and movement.",
                Span = 2,
                RequiredPermission = Permissions.ScorecardView
            },
            [WidgetScorecardHealth] = new()
            {
                Key = WidgetScorecardHealth,
                Title = "Scorecard health",
                Description = "Corporate and division scorecards with plan completeness.",
                Span = 2,
                RequiredPermission = Permissions.ScorecardView
            },
            [WidgetGoalHealth] = new()
            {
                Key = WidgetGoalHealth,
                Title = "Goal health overview",
                Description = "Goal, score, RAG and trend. Select a goal to drill into its KRAs.",
                Span = 2,
                RequiredPermission = Permissions.GoalsView
            },
            [WidgetGoalPerformance] = new()
            {
                Key = WidgetGoalPerformance,
                Title = "Goal performance",
                Description = "Read-only goal score and RAG for strategic review.",
                Span = 2,
                RequiredPermission = Permissions.GoalsView
            },
            [WidgetGoalPerformanceChart] = new()
            {
                Key = WidgetGoalPerformanceChart,
                Title = "Goal achievement",
                Description = "Every goal ranked by weighted achievement for the selected period.",
                Span = 2,
                RequiredPermission = Permissions.GoalsView
            },
            [WidgetTopBottomKpis] = new()
            {
                Key = WidgetTopBottomKpis,
                Title = "Top and bottom KPIs",
                Description = "Highest and lowest performing KPIs for the selected period.",
                Span = 2,
                RequiredPermission = Permissions.KpiView
            },
            [WidgetKpiExceptions] = new()
            {
                Key = WidgetKpiExceptions,
                Title = "KPI exceptions",
                Description = "KPIs off track or without data that need management attention.",
                Span = 2,
                RequiredPermission = Permissions.KpiView
            },
            [WidgetStrategicAlignment] = new()
            {
                Key = WidgetStrategicAlignment,
                Title = "Strategic alignment",
                Description = "Goal to KRA to KPI relationships and their contribution.",
                Span = 2,
                RequiredPermission = Permissions.GoalsView
            },
            [WidgetInitiativeStatus] = new()
            {
                Key = WidgetInitiativeStatus,
                Title = "Initiative status",
                Description = "Not started, in progress, completed, on hold and cancelled initiatives.",
                Span = 2,
                RequiredPermission = Permissions.InitiativesView
            },
            [WidgetInitiativeDeliveryChart] = new()
            {
                Key = WidgetInitiativeDeliveryChart,
                Title = "Initiative delivery",
                Description = "Weighted milestone delivery for the largest active initiatives.",
                Span = 2,
                RequiredPermission = Permissions.InitiativesView
            },
            [WidgetMilestoneTrend] = new()
            {
                Key = WidgetMilestoneTrend,
                Title = "Milestone completion",
                Description = "Planned versus actual milestone progress across the portfolio.",
                Span = 2,
                RequiredPermission = Permissions.InitiativesView
            },
            [WidgetBudgetSummary] = new()
            {
                Key = WidgetBudgetSummary,
                Title = "Budget summary",
                Description = "Planned budget by initiative status and priority, with variance flags.",
                Span = 2,
                RequiredPermission = Permissions.InitiativesView
            },
            [WidgetBudgetAllocationChart] = new()
            {
                Key = WidgetBudgetAllocationChart,
                Title = "Budget allocation",
                Description = "Planned budget grouped by priority and initiative status.",
                Span = 2,
                RequiredPermission = Permissions.InitiativesView
            },
            [WidgetDivisionComparison] = new()
            {
                Key = WidgetDivisionComparison,
                Title = "Division comparison",
                Description = "Division plan scores for the selected period.",
                Span = 2,
                RequiredPermission = Permissions.DivisionView
            },
            [WidgetDivisionPerformanceChart] = new()
            {
                Key = WidgetDivisionPerformanceChart,
                Title = "Division performance",
                Description = "Divisions ranked by weighted plan score for the selected period.",
                Span = 2,
                RequiredPermission = Permissions.DivisionView
            },
            [WidgetTargetVsActual] = new()
            {
                Key = WidgetTargetVsActual,
                Title = "Target vs actual",
                Description = "Configured targets against entered actuals for the same period, grouped by unit.",
                Span = 3,
                RequiredPermission = Permissions.KpiView
            },
            [WidgetAssignedKpis] = new()
            {
                Key = WidgetAssignedKpis,
                Title = "My KPIs",
                Description = "Actual against target for the KPIs assigned to you, with data-entry status.",
                Span = 3,
                RequiredPermission = Permissions.ActualsView
            },
            [WidgetDataEntryStatus] = new()
            {
                Key = WidgetDataEntryStatus,
                Title = "Data entry status",
                Description = "How much of the current period has been entered and what is still missing.",
                Span = 2,
                RequiredPermission = Permissions.ActualsView
            },
            [WidgetPeriodLocks] = new()
            {
                Key = WidgetPeriodLocks,
                Title = "Period locking",
                Description = "Entry windows, cutoffs and lock state for every fiscal month.",
                Span = 2,
                RequiredPermission = Permissions.ActualsView
            },
            [WidgetSystemStats] = new()
            {
                Key = WidgetSystemStats,
                Title = "System statistics",
                Description = "Users, roles and planning object counts held by the system.",
                Span = 1,
                RequiredPermission = Permissions.AdminUsersManage
            },
            [WidgetAuditActivity] = new()
            {
                Key = WidgetAuditActivity,
                Title = "Recent audit activity",
                Description = "Security relevant changes recorded by the audit interceptor.",
                Span = 2,
                RequiredPermission = Permissions.AdminAuditView
            },
            [WidgetReportsSummary] = new()
            {
                Key = WidgetReportsSummary,
                Title = "Performance summaries",
                Description = "Read-only report views available to this role.",
                Span = 2,
                RequiredPermission = Permissions.ReportsView
            },
            [WidgetAlerts] = new()
            {
                Key = WidgetAlerts,
                Title = "Alerts requiring attention",
                Description = "Rule based alerts limited to the data this role is permitted to see.",
                Span = 2
            }
        };

    private static DashboardQuickActionDefinition Action(
        string key, string label, string route, string icon, string? permission = null, string? emphasis = null)
        => new() { Key = key, Label = label, Route = route, Icon = icon, RequiredPermission = permission, Emphasis = emphasis };

    public static readonly IReadOnlyDictionary<string, DashboardRoleProfile> Profiles =
        new Dictionary<string, DashboardRoleProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [Roles.Admin] = new()
            {
                Role = Roles.Admin,
                DisplayName = "Administration",
                Purpose = "System health, security, configuration and organization wide performance.",
                Insight = "Monitor platform health and resolve configuration or data quality issues.",
                DataScope = DashboardDataScope.OrganizationWide,
                Widgets = new[]
                {
                    WidgetKpiSummary, WidgetOverallScore, WidgetRagDistribution, WidgetScorecardHealth,
                    WidgetScorecardPerformance, WidgetTargetVsActual, WidgetGoalPerformanceChart,
                    WidgetDataEntryStatus, WidgetInitiativeStatus, WidgetInitiativeDeliveryChart,
                    WidgetBudgetSummary, WidgetBudgetAllocationChart, WidgetDivisionComparison,
                    WidgetDivisionPerformanceChart, WidgetSystemStats, WidgetAuditActivity,
                    WidgetAlerts, WidgetReportsSummary
                },
                QuickActions = new[]
                {
                    Action("users", "User management", "/admin/users", "users", Permissions.AdminUsersManage),
                    Action("roles", "Roles and permissions", "/admin/roles", "shield", Permissions.AdminRolesManage),
                    Action("config", "System configuration", "/admin/fiscal-years", "settings", Permissions.AdminConfigManage),
                    Action("audit", "Audit log", "/data-locking", "history", Permissions.AdminAuditView),
                    Action("data", "Data management", "/data-management", "database", Permissions.DataUpload),
                    Action("scorecards", "Scorecards", "/scorecards", "target", Permissions.ScorecardView),
                    Action("reports", "Reports", "/reports", "chart", Permissions.ReportsView)
                }
            },

            [Roles.PowerUser] = new()
            {
                Role = Roles.PowerUser,
                DisplayName = "Strategic management",
                Purpose = "Strategic planning, performance management and exception monitoring.",
                Insight = "Keep the plan on track: review goal health, exceptions and initiative delivery.",
                DataScope = DashboardDataScope.OrganizationWide,
                Widgets = new[]
                {
                    WidgetKpiSummary, WidgetOverallScore, WidgetRagDistribution, WidgetPerformanceTrend,
                    WidgetScorecardPerformance, WidgetTargetVsActual, WidgetGoalHealth,
                    WidgetGoalPerformanceChart, WidgetTopBottomKpis, WidgetInitiativeStatus,
                    WidgetInitiativeDeliveryChart, WidgetStrategicAlignment, WidgetBudgetSummary,
                    WidgetBudgetAllocationChart, WidgetMilestoneTrend, WidgetDivisionComparison,
                    WidgetDivisionPerformanceChart, WidgetAlerts
                },
                QuickActions = new[]
                {
                    Action("scorecards", "Scorecard management", "/scorecards/manage", "target", Permissions.ScorecardView),
                    Action("goals", "Goal management", "/goals", "goal", Permissions.GoalsView),
                    Action("kras", "KRA management", "/kras", "kra", Permissions.KraView),
                    Action("kpis", "KPI management", "/kpis", "kpi", Permissions.KpiView),
                    Action("targets", "Target configuration", "/target-config", "target", Permissions.TargetsEdit),
                    Action("entry", "Data entry review", "/data-entry", "edit", Permissions.ActualsEnter),
                    Action("initiatives", "Initiatives", "/initiatives", "flag", Permissions.InitiativesView),
                    Action("reports", "Reports", "/reports", "chart", Permissions.ReportsView)
                }
            },

            [Roles.Executive] = new()
            {
                Role = Roles.Executive,
                DisplayName = "Executive overview",
                Purpose = "Fast organization wide strategic decision support.",
                Insight = "Strategic performance at a glance with the exceptions that need a decision.",
                DataScope = DashboardDataScope.OrganizationWide,
                IsReadOnly = true,
                Widgets = new[]
                {
                    WidgetKpiSummary, WidgetOverallScore, WidgetRagDistribution, WidgetPerformanceTrend,
                    WidgetScorecardPerformance, WidgetScorecardHealth, WidgetTargetVsActual,
                    WidgetGoalPerformance, WidgetGoalPerformanceChart, WidgetKpiExceptions,
                    WidgetDivisionComparison, WidgetDivisionPerformanceChart, WidgetInitiativeStatus,
                    WidgetInitiativeDeliveryChart, WidgetBudgetSummary, WidgetBudgetAllocationChart,
                    WidgetAlerts, WidgetReportsSummary
                },
                QuickActions = new[]
                {
                    Action("performance", "Scorecard performance", "/scorecardss/performance", "target", Permissions.ScorecardView),
                    Action("reports", "Reports", "/reports", "chart", Permissions.ReportsView),
                    Action("initiatives", "Initiatives", "/initiatives", "flag", Permissions.InitiativesView)
                }
            },

            [Roles.DataInputUser] = new()
            {
                Role = Roles.DataInputUser,
                DisplayName = "Data entry",
                Purpose = "Enter and validate actuals for the KPIs assigned to you.",
                Insight = "Submit actuals before the cutoff: only your assigned KPIs and initiatives are shown.",
                DataScope = DashboardDataScope.AssignedOnly,
                Widgets = new[]
                {
                    WidgetKpiSummary, WidgetTargetVsActual, WidgetAssignedKpis, WidgetDataEntryStatus,
                    WidgetPeriodLocks, WidgetInitiativeStatus, WidgetInitiativeDeliveryChart, WidgetAlerts
                },
                QuickActions = new[]
                {
                    Action("enter", "Enter data", "/data-entry", "edit", Permissions.ActualsEnter, "primary"),
                    Action("lockstatus", "Data locking", "/data-locking", "lock", Permissions.ActualsView),
                    Action("division", "Division plan", "/divisions", "division", Permissions.DivisionView)
                }
            },

            [Roles.ViewOnly] = new()
            {
                Role = Roles.ViewOnly,
                DisplayName = "Performance view",
                Purpose = "Read-only performance visibility across the strategy.",
                Insight = "Read-only view of strategic performance; no data entry or configuration actions.",
                DataScope = DashboardDataScope.OrganizationWide,
                IsReadOnly = true,
                Widgets = new[]
                {
                    WidgetKpiSummary, WidgetOverallScore, WidgetRagDistribution, WidgetPerformanceTrend,
                    WidgetScorecardPerformance, WidgetTargetVsActual, WidgetGoalPerformance,
                    WidgetGoalPerformanceChart, WidgetInitiativeStatus, WidgetInitiativeDeliveryChart,
                    WidgetDivisionComparison, WidgetDivisionPerformanceChart, WidgetReportsSummary
                },
                QuickActions = new[]
                {
                    Action("performance", "Scorecard performance", "/scorecardss/performance", "target", Permissions.ScorecardView),
                    Action("reports", "Reports", "/reports", "chart", Permissions.ReportsView),
                    Action("initiatives", "Initiatives", "/initiatives", "flag", Permissions.InitiativesView)
                }
            }
        };

    /// <summary>
    /// Resolves the dashboard profile for the authenticated roles. The most privileged
    /// matching role wins so a user with several roles still gets a single coherent shell.
    /// Unknown or role-less callers fall back to the read-only profile.
    /// </summary>
    public static DashboardRoleProfile Resolve(IReadOnlyList<string> roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return Profiles[Roles.ViewOnly];
        }

        var ordered = new[] { Roles.Admin, Roles.PowerUser, Roles.Executive, Roles.DataInputUser, Roles.ViewOnly };

        foreach (var candidate in ordered)
        {
            if (roles.Any(r => string.Equals(r, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return Profiles[candidate];
            }
        }

        return Profiles[Roles.ViewOnly];
    }

    /// <summary>Widget definitions a role may receive, intersected with the caller's permissions.</summary>
    public static IReadOnlyList<DashboardWidgetDefinition> ResolveWidgets(
        DashboardRoleProfile profile, IReadOnlyCollection<string> permissions)
    {
        var granted = new HashSet<string>(permissions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        return profile.Widgets
            .Select(key => Widgets.TryGetValue(key, out var definition) ? definition : null)
            .Where(definition => definition is not null)
            .Select(definition => definition!)
            .Where(definition => string.IsNullOrEmpty(definition.RequiredPermission) || granted.Contains(definition.RequiredPermission))
            .ToList();
    }

    /// <summary>Quick actions a role may see, filtered by the caller's permissions.</summary>
    public static IReadOnlyList<DashboardQuickActionDefinition> ResolveActions(
        DashboardRoleProfile profile, IReadOnlyCollection<string> permissions)
    {
        var granted = new HashSet<string>(permissions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        return profile.QuickActions
            .Where(action => string.IsNullOrEmpty(action.RequiredPermission) || granted.Contains(action.RequiredPermission))
            .ToList();
    }
}
