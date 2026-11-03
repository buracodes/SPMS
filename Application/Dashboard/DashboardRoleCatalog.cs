using Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Dashboard;

/// <summary>How far the dashboard may see into the strategy data set.</summary>
public enum DashboardDataScope
{
    /// <summary>Every scorecard, goal, KPI and initiative the effective permissions allow.</summary>
    OrganizationWide = 1,

    /// <summary>The employee's own HR-synchronized division only.</summary>
    Division = 2,

    /// <summary>Only records assigned to the signed-in user (their KPIs and initiatives).</summary>
    AssignedOnly = 3
}

/// <summary>A single dashboard widget the shell can render.</summary>
public sealed class DashboardWidgetDefinition
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Grid width: 1 = third, 2 = two thirds, 3 = full width.</summary>
    public int Span { get; init; } = 2;

    /// <summary>Any-of permission gate. Empty means every dashboard viewer receives the widget.</summary>
    public IReadOnlyList<string> RequiresAnyPermission { get; init; } = Array.Empty<string>();

    /// <summary>False for widgets whose payload is meaningless without corporate visibility.</summary>
    public bool OrganizationScope { get; init; } = true;

    /// <summary>True when the payload is already limited to the employee's own division.</summary>
    public bool DivisionScope { get; init; } = true;

    /// <summary>True only for the task-oriented widgets an assigned-only user needs.</summary>
    public bool AssignedScope { get; init; }
}

/// <summary>A navigation action offered to the caller, permission checked server side.</summary>
public sealed class DashboardQuickActionDefinition
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Route { get; init; }

    public required string Icon { get; init; }

    public string? RequiredPermission { get; init; }

    public string? Emphasis { get; init; }
}

/// <summary>
/// The resolved dashboard configuration. It is derived from the caller's effective
/// permissions and HR division scope, never from a role name: a user in several groups
/// gets the union of their rights, and the HR division claim always narrows the data set.
/// The role/display fields below are labels only and grant nothing.
/// </summary>
public sealed class DashboardRoleProfile
{
    public required string Role { get; init; }

    public required string DisplayName { get; init; }

    public required string Purpose { get; init; }

    public required string Insight { get; init; }

    public required DashboardDataScope DataScope { get; init; }

    /// <summary>True when the caller holds no write right of any kind.</summary>
    public bool IsReadOnly { get; init; }

    public required IReadOnlyList<DashboardWidgetDefinition> Widgets { get; init; }

    public required IReadOnlyList<DashboardQuickActionDefinition> QuickActions { get; init; }
}

/// <summary>
/// Server-side dashboard composition. The shell is shared; this catalog decides which
/// widgets and actions the caller may receive from their effective permissions and scope.
/// Widget payloads are still filtered again by the dashboard service, and every API endpoint
/// keeps enforcing its own policy, so this table is presentation - not authorization.
/// </summary>
public static class DashboardRoleCatalog
{
    public const string WidgetKpiSummary = "kpiSummary";
    public const string WidgetOverallScore = "overallScore";
    public const string WidgetRagDistribution = "ragDistribution";
    public const string WidgetPerformanceTrend = "performanceTrend";
    public const string WidgetKpiPerformanceTrend = "kpiPerformanceTrend";
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

    private static readonly string[] ExchangeRead =
        { Permissions.KpiView, Permissions.DivisionScorecardView, Permissions.DivisionPerformanceView };

    private static readonly string[] ExchangeGoalRead =
        { Permissions.GoalsView, Permissions.DivisionScorecardView, Permissions.DivisionPerformanceView };

    private static readonly string[] ExchangeInitiativeRead =
        { Permissions.InitiativesView, Permissions.DivisionInitiativeView };

    private static readonly string[] ExchangeActualEntry =
        { Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DivisionActualsEnter };

    // Period locking is an entry-window concern. A read-only viewer gets the scores and RAG,
    // not the open/closed controls, so a division performance right is deliberately absent.
    private static readonly string[] ExchangePeriodView =
        { Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DivisionActualsEnter };

    public static readonly IReadOnlyDictionary<string, DashboardWidgetDefinition> Widgets =
        new Dictionary<string, DashboardWidgetDefinition>(StringComparer.Ordinal)
        {
            [WidgetKpiSummary] = new()
            {
                Key = WidgetKpiSummary,
                Title = "Performance summary",
                Description = "Headline performance for the selected fiscal year, scorecard and period.",
                Span = 3,
                AssignedScope = true
            },
            [WidgetOverallScore] = new()
            {
                Key = WidgetOverallScore,
                Title = "Overall score",
                Description = "Weighted score produced by the SPMS calculation engine.",
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
                Description = "Month by month score movement across the fiscal year.",
                Span = 2
            },
            [WidgetKpiPerformanceTrend] = new()
            {
                Key = WidgetKpiPerformanceTrend,
                Title = "KPI performance trend",
                Description = "Month by month score for the most material KPIs in scope.",
                Span = 2,
                RequiresAnyPermission = ExchangeRead
            },
            [WidgetScorecardPerformance] = new()
            {
                Key = WidgetScorecardPerformance,
                Title = "Scorecard performance",
                Description = "Every scorecard in scope, with score, RAG and movement.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.ScorecardView, Permissions.DivisionScorecardView }
            },
            [WidgetScorecardHealth] = new()
            {
                Key = WidgetScorecardHealth,
                Title = "Scorecard health",
                Description = "Corporate and division scorecards with plan completeness.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.ScorecardView },
                DivisionScope = false
            },
            [WidgetGoalHealth] = new()
            {
                Key = WidgetGoalHealth,
                Title = "Goal health overview",
                Description = "Goal, score, RAG and trend. Select a goal to drill into its KRAs.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.GoalsView },
                DivisionScope = false
            },
            [WidgetGoalPerformance] = new()
            {
                Key = WidgetGoalPerformance,
                Title = "Goal performance",
                Description = "Goal achievement and status for the selected period.",
                Span = 2,
                RequiresAnyPermission = ExchangeGoalRead
            },
            [WidgetGoalPerformanceChart] = new()
            {
                Key = WidgetGoalPerformanceChart,
                Title = "Goal achievement",
                Description = "Every goal ranked by weighted achievement for the selected period.",
                Span = 2,
                RequiresAnyPermission = ExchangeGoalRead
            },
            [WidgetTopBottomKpis] = new()
            {
                Key = WidgetTopBottomKpis,
                Title = "Top and bottom KPIs",
                Description = "Highest and lowest performing KPIs for the selected period.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.KpiView },
                DivisionScope = false
            },
            [WidgetKpiExceptions] = new()
            {
                Key = WidgetKpiExceptions,
                Title = "KPI exceptions",
                Description = "KPIs off track or without data that need management attention.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.KpiView },
                DivisionScope = false
            },
            [WidgetStrategicAlignment] = new()
            {
                Key = WidgetStrategicAlignment,
                Title = "Strategic alignment",
                Description = "Goal to KRA to KPI relationships and their contribution.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.GoalsView },
                DivisionScope = false
            },
            [WidgetInitiativeStatus] = new()
            {
                Key = WidgetInitiativeStatus,
                Title = "Initiative status",
                Description = "Not started, in progress, completed, on hold and cancelled initiatives.",
                Span = 2,
                RequiresAnyPermission = ExchangeInitiativeRead
            },
            [WidgetInitiativeDeliveryChart] = new()
            {
                Key = WidgetInitiativeDeliveryChart,
                Title = "Initiative delivery",
                Description = "Weighted milestone delivery for the largest active initiatives in scope.",
                Span = 2,
                RequiresAnyPermission = ExchangeInitiativeRead
            },
            [WidgetMilestoneTrend] = new()
            {
                Key = WidgetMilestoneTrend,
                Title = "Milestone completion",
                Description = "Planned versus actual milestone progress across the portfolio.",
                Span = 2,
                RequiresAnyPermission = ExchangeInitiativeRead
            },
            [WidgetBudgetSummary] = new()
            {
                Key = WidgetBudgetSummary,
                Title = "Budget summary",
                Description = "Planned budget by initiative status and priority, with variance flags.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.InitiativesView }
            },
            [WidgetBudgetAllocationChart] = new()
            {
                Key = WidgetBudgetAllocationChart,
                Title = "Budget allocation",
                Description = "Planned budget grouped by priority and initiative status.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.InitiativesView }
            },
            [WidgetDivisionComparison] = new()
            {
                Key = WidgetDivisionComparison,
                Title = "Division comparison",
                Description = "Division plan scores for the selected period.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.PerformanceMonitorView },
                DivisionScope = false
            },
            [WidgetDivisionPerformanceChart] = new()
            {
                Key = WidgetDivisionPerformanceChart,
                Title = "Division performance",
                Description = "Divisions ranked by weighted plan score for the selected period.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.PerformanceMonitorView },
                DivisionScope = false
            },
            [WidgetTargetVsActual] = new()
            {
                Key = WidgetTargetVsActual,
                Title = "Target vs actual",
                Description = "Configured targets against entered actuals for the same period, grouped by unit.",
                Span = 3,
                RequiresAnyPermission = ExchangeRead.Concat(new[] { Permissions.ActualsView }).Distinct().ToArray()
            },
            [WidgetAssignedKpis] = new()
            {
                Key = WidgetAssignedKpis,
                Title = "My KPIs",
                Description = "Actual against target with data-entry status and the current period deadline.",
                Span = 3,
                RequiresAnyPermission = ExchangeActualEntry,
                AssignedScope = true
            },
            [WidgetDataEntryStatus] = new()
            {
                Key = WidgetDataEntryStatus,
                Title = "Data entry status",
                Description = "How much of the current period has been entered and what is still missing.",
                Span = 2,
                RequiresAnyPermission = ExchangeActualEntry,
                AssignedScope = true
            },
            [WidgetPeriodLocks] = new()
            {
                Key = WidgetPeriodLocks,
                Title = "Period locking",
                Description = "Entry windows, cutoffs and lock state for every fiscal month.",
                Span = 2,
                RequiresAnyPermission = ExchangePeriodView,
                AssignedScope = true
            },
            [WidgetSystemStats] = new()
            {
                Key = WidgetSystemStats,
                Title = "System statistics",
                Description = "Users, roles and planning object counts held by the system.",
                Span = 1,
                RequiresAnyPermission = new[] { Permissions.AdminUsersManage },
                DivisionScope = false,
                AssignedScope = false
            },
            [WidgetAuditActivity] = new()
            {
                Key = WidgetAuditActivity,
                Title = "Recent audit activity",
                Description = "Security relevant changes recorded by the audit interceptor.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.AdminAuditView },
                DivisionScope = false,
                AssignedScope = false
            },
            [WidgetReportsSummary] = new()
            {
                Key = WidgetReportsSummary,
                Title = "Performance summaries",
                Description = "Read-only report views available to this caller.",
                Span = 2,
                RequiresAnyPermission = new[] { Permissions.ReportsView }
            },
            [WidgetAlerts] = new()
            {
                Key = WidgetAlerts,
                Title = "Alerts requiring attention",
                Description = "Rule based alerts limited to the data this caller may see.",
                Span = 2,
                AssignedScope = true
            }
        };

    /// <summary>Layout order for organization and division dashboards.</summary>
    private static readonly string[] OrganizationOrder =
    {
        WidgetKpiSummary, WidgetOverallScore, WidgetRagDistribution, WidgetPerformanceTrend,
        WidgetKpiPerformanceTrend, WidgetScorecardPerformance, WidgetScorecardHealth,
        WidgetGoalHealth, WidgetGoalPerformance, WidgetGoalPerformanceChart, WidgetTargetVsActual,
        WidgetTopBottomKpis, WidgetKpiExceptions, WidgetStrategicAlignment,
        WidgetInitiativeStatus, WidgetInitiativeDeliveryChart, WidgetMilestoneTrend,
        WidgetBudgetSummary, WidgetBudgetAllocationChart,
        WidgetDivisionComparison, WidgetDivisionPerformanceChart,
        WidgetAssignedKpis, WidgetDataEntryStatus, WidgetPeriodLocks,
        WidgetSystemStats, WidgetAuditActivity, WidgetReportsSummary, WidgetAlerts
    };

    /// <summary>Task-oriented layout for an assigned-only data-entry account.</summary>
    private static readonly string[] AssignedOrder =
    {
        WidgetKpiSummary, WidgetAssignedKpis, WidgetDataEntryStatus, WidgetPeriodLocks,
        WidgetTargetVsActual, WidgetKpiPerformanceTrend, WidgetPerformanceTrend,
        WidgetRagDistribution, WidgetOverallScore, WidgetInitiativeStatus, WidgetMilestoneTrend,
        WidgetReportsSummary, WidgetAlerts
    };

    private static readonly string[] WritePermissions =
    {
        Permissions.OrganizationEdit,
        Permissions.ScorecardCreate, Permissions.ScorecardEdit, Permissions.ScorecardClose,
        Permissions.ScorecardClone, Permissions.ScorecardDelete,
        Permissions.GoalsCreate, Permissions.GoalsEdit, Permissions.GoalsDelete,
        Permissions.KraCreate, Permissions.KraEdit, Permissions.KraDelete,
        Permissions.KpiCreate, Permissions.KpiEdit, Permissions.KpiDelete, Permissions.KpiOverride,
        Permissions.TargetsEdit,
        Permissions.ActualsEnter, Permissions.ActualsOverride,
        Permissions.InitiativesCreate, Permissions.InitiativesEdit, Permissions.InitiativesDelete,
        Permissions.InitiativesUpdateProgress, Permissions.InitiativesManageMilestones,
        Permissions.InitiativesManageDependencies, Permissions.InitiativesUpdateExpenditure,
        Permissions.InitiativesClose,
        Permissions.DataUpload, Permissions.DataValidate,
        Permissions.DivisionManage, Permissions.DivisionPlan, Permissions.DivisionActualsEnter,
        Permissions.AdminUsersManage, Permissions.AdminRolesManage, Permissions.AdminConfigManage,
        Permissions.AdminRagThresholdsManage, Permissions.AdminAccessGroupsManage,
        Permissions.AdminRightsManage, Permissions.AdminUserRightsManage
    };

    private static readonly string[] ActualEntryPermissions =
    {
        Permissions.ActualsEnter, Permissions.DivisionActualsEnter
    };

    /// <summary>
    /// Resolves the dashboard layout from effective permissions and the HR division claim.
    /// A division claim always narrows the data set, even when the caller also holds broad
    /// read rights; a user without one only gains organization-wide data through a read
    /// permission, and a pure data-entry account stays assigned-only.
    /// The optional role labels only pick the display persona; they never influence the
    /// data scope, widgets or quick actions.
    /// </summary>
    public static DashboardRoleProfile Resolve(
        IReadOnlyCollection<string> permissions,
        Guid? divisionId,
        IReadOnlyCollection<string>? roleLabels = null)
    {
        var granted = new HashSet<string>(permissions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var scope = ResolveScope(granted, divisionId);
        var readOnly = !WritePermissions.Any(granted.Contains);
        var order = scope == DashboardDataScope.AssignedOnly ? AssignedOrder : OrganizationOrder;

        var widgets = order
            .Select(key => Widgets.TryGetValue(key, out var definition) ? definition : null)
            .Where(definition => definition is not null)
            .Select(definition => definition!)
            .Where(definition => AllowsScope(definition, scope))
            .Where(definition => definition.RequiresAnyPermission.Count == 0
                                 || definition.RequiresAnyPermission.Any(granted.Contains))
            .ToList();

        var persona = Describe(scope, readOnly, granted, roleLabels);

        return new DashboardRoleProfile
        {
            Role = persona.Role,
            DisplayName = persona.DisplayName,
            Purpose = persona.Purpose,
            Insight = persona.Insight,
            DataScope = scope,
            IsReadOnly = readOnly,
            Widgets = widgets,
            QuickActions = BuildActions(scope, granted)
        };
    }

    /// <summary>Convenience overload used by the widget service and tests.</summary>
    public static IReadOnlyList<DashboardWidgetDefinition> ResolveWidgets(
        IReadOnlyCollection<string> permissions,
        Guid? divisionId)
        => Resolve(permissions, divisionId).Widgets;

    private static DashboardDataScope ResolveScope(IReadOnlySet<string> granted, Guid? divisionId)
    {
        if (divisionId.HasValue)
        {
            return DashboardDataScope.Division;
        }

        var hasActualEntry = ActualEntryPermissions.Any(granted.Contains);
        var hasBroadRead = granted.Contains(Permissions.ScorecardView)
                           || granted.Contains(Permissions.PerformanceMonitorView);

        // A data-entry account that is not pinned to a division works from its assignments,
        // never from the whole organization: an entry right is not a read-all right.
        if (hasActualEntry && !granted.Contains(Permissions.PerformanceMonitorView))
        {
            return DashboardDataScope.AssignedOnly;
        }

        return hasBroadRead ? DashboardDataScope.OrganizationWide : DashboardDataScope.AssignedOnly;
    }

    private static bool AllowsScope(DashboardWidgetDefinition definition, DashboardDataScope scope) => scope switch
    {
        DashboardDataScope.OrganizationWide => definition.OrganizationScope,
        DashboardDataScope.Division => definition.DivisionScope,
        DashboardDataScope.AssignedOnly => definition.AssignedScope,
        _ => false
    };

    private static bool HasAny(IReadOnlySet<string> granted, params string[] permissions)
        => permissions.Any(granted.Contains);

    private static bool HasRole(IReadOnlyCollection<string>? roleLabels, string role)
        => roleLabels is not null && roleLabels.Contains(role, StringComparer.OrdinalIgnoreCase);

    private static DashboardQuickActionDefinition Action(
        string key, string label, string route, string icon, string? permission = null, string? emphasis = null)
        => new() { Key = key, Label = label, Route = route, Icon = icon, RequiredPermission = permission, Emphasis = emphasis };

    private static IReadOnlyList<DashboardQuickActionDefinition> BuildActions(
        DashboardDataScope scope, IReadOnlySet<string> granted)
    {
        var actions = new List<DashboardQuickActionDefinition>();

        void Add(DashboardQuickActionDefinition action)
        {
            if (!actions.Any(a => a.Key == action.Key))
            {
                actions.Add(action);
            }
        }

        if (HasAny(granted, Permissions.ScorecardView))
        {
            Add(Action("performance", "Scorecard performance", "/scorecardss/performance", "target", Permissions.ScorecardView));
        }
        else if (scope == DashboardDataScope.Division && HasAny(granted, Permissions.DivisionScorecardView))
        {
            Add(Action("performance", "Division scorecard", "/division-planning", "target", Permissions.DivisionScorecardView));
        }

        if (HasAny(granted, Permissions.ReportsView))
        {
            Add(Action("reports", "Reports", "/reports", "chart", Permissions.ReportsView));
        }

        if (HasAny(granted, Permissions.InitiativesView, Permissions.DivisionInitiativeView))
        {
            Add(Action("initiatives", "Initiatives", "/initiatives", "flag",
                HasAny(granted, Permissions.InitiativesView) ? Permissions.InitiativesView : Permissions.DivisionInitiativeView));
        }

        if (HasAny(granted, Permissions.ActualsEnter))
        {
            Add(Action("entry", "Enter data", "/data-entry", "edit", Permissions.ActualsEnter, "primary"));
        }
        else if (scope == DashboardDataScope.Division && HasAny(granted, Permissions.DivisionActualsEnter))
        {
            Add(Action("entry", "Enter division data", "/division-planning", "edit", Permissions.DivisionActualsEnter, "primary"));
        }

        if (HasAny(granted, Permissions.ActualsView))
        {
            Add(Action("lockstatus", "Data locking", "/data-locking", "lock", Permissions.ActualsView));
        }

        if (HasAny(granted, Permissions.ScorecardView))
        {
            Add(Action("scorecards", "Scorecard management", "/scorecards/manage", "target", Permissions.ScorecardView));
        }

        if (HasAny(granted, Permissions.GoalsView))
        {
            Add(Action("goals", "Goal management", "/goals", "goal", Permissions.GoalsView));
        }

        if (HasAny(granted, Permissions.KraView))
        {
            Add(Action("kras", "KRA management", "/kras", "kra", Permissions.KraView));
        }

        if (HasAny(granted, Permissions.KpiView))
        {
            Add(Action("kpis", "KPI management", "/kpis", "kpi", Permissions.KpiView));
        }

        if (HasAny(granted, Permissions.TargetsEdit))
        {
            Add(Action("targets", "Target configuration", "/target-config", "target", Permissions.TargetsEdit));
        }

        if (HasAny(granted, Permissions.DivisionPlan, Permissions.DivisionManage))
        {
            Add(Action("divisions", "Division planning", "/divisions", "division",
                HasAny(granted, Permissions.DivisionPlan) ? Permissions.DivisionPlan : Permissions.DivisionManage));
        }

        if (HasAny(granted, Permissions.AdminUsersManage))
        {
            Add(Action("users", "User management", "/admin/users", "users", Permissions.AdminUsersManage));
        }

        if (HasAny(granted, Permissions.AdminRolesManage))
        {
            Add(Action("roles", "Roles and permissions", "/admin/roles", "shield", Permissions.AdminRolesManage));
        }

        if (HasAny(granted, Permissions.AdminConfigManage))
        {
            Add(Action("config", "System configuration", "/admin/fiscal-years", "settings", Permissions.AdminConfigManage));
        }

        if (HasAny(granted, Permissions.AdminAuditView))
        {
            Add(Action("audit", "Audit log", "/data-locking", "history", Permissions.AdminAuditView));
        }

        if (HasAny(granted, Permissions.AdminAccessView))
        {
            Add(Action("access", "Access control", "/admin/access-control", "shield", Permissions.AdminAccessView));
        }

        if (HasAny(granted, Permissions.DataUpload))
        {
            Add(Action("data", "Data management", "/data-management", "database", Permissions.DataUpload));
        }

        return actions;
    }

    private sealed record Persona(string Role, string DisplayName, string Purpose, string Insight);

    /// <summary>
    /// Display text only. The persona never feeds back into the data scope, widgets or
    /// actions; those are computed from permissions and the division claim alone. The
    /// organization-wide Executive label requires the explicitly assigned Executive role,
    /// because reporting or configuration rights do not make a caller an executive.
    /// </summary>
    private static Persona Describe(
        DashboardDataScope scope,
        bool readOnly,
        IReadOnlySet<string> granted,
        IReadOnlyCollection<string>? roleLabels)
    {
        if (HasAny(granted, Permissions.AdminUsersManage, Permissions.AdminRolesManage,
                Permissions.AdminConfigManage, Permissions.AdminAccessGroupsManage))
        {
            return new Persona(
                Roles.Admin,
                "Administration",
                "System health, security, configuration and organization wide performance.",
                "Monitor platform health and resolve configuration or data quality issues.");
        }

        if (scope == DashboardDataScope.Division)
        {
            if (HasAny(granted, ActualEntryPermissions))
            {
                return new Persona(
                    Roles.DataInputUser,
                    "Division data entry",
                    "Enter actuals for the KPIs in your division plan and follow their performance.",
                    "Submit actuals before the cutoff; only your division is shown.");
            }

            return new Persona(
                "DivisionView",
                "Division performance",
                "Read-only performance for your own division scorecard, goals, KPIs and initiatives.",
                "Your division at a glance; no data entry or configuration actions.");
        }

        if (scope == DashboardDataScope.AssignedOnly)
        {
            if (HasAny(granted, ActualEntryPermissions))
            {
                return new Persona(
                    Roles.DataInputUser,
                    "Data entry",
                    "Enter and validate actuals for the KPIs assigned to you.",
                    "Submit actuals before the cutoff: only your assigned KPIs are shown.");
            }

            return new Persona(
                Roles.ViewOnly,
                "Performance view",
                "Read-only performance visibility within your authorized scope.",
                "Read-only view of strategic performance; no data entry or configuration actions.");
        }

        if (readOnly && HasRole(roleLabels, Roles.Executive))
        {
            return new Persona(
                Roles.Executive,
                "Executive overview",
                "Fast organization wide strategic decision support.",
                "Strategic performance at a glance with the exceptions that need a decision.");
        }

        if (HasAny(granted, Permissions.PerformanceMonitorView))
        {
            return new Persona(
                Roles.PowerUser,
                "Strategic management",
                "Strategic planning, performance management and exception monitoring.",
                "Keep the plan on track: review goal health, exceptions and initiative delivery.");
        }

        return new Persona(
            Roles.ViewOnly,
            "Performance view",
            "Read-only performance visibility across the strategy.",
            "Read-only view of strategic performance; no data entry or configuration actions.");
    }
}
