using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Constants
{
    public record PermissionDefinition(string Name, string Module, string Description);

    public static class Permissions
    {
        public const string OrganizationView = "organization:view";
        public const string OrganizationEdit = "organization:edit";

        public const string ScorecardView = "scorecard:view";
        public const string ScorecardCreate = "scorecard:create";
        public const string ScorecardEdit = "scorecard:edit";
        public const string ScorecardClose = "scorecard:close";
        public const string ScorecardClone = "scorecard:clone";
        public const string ScorecardDelete = "scorecard:delete";

        public const string GoalsView = "goals:view";
        public const string GoalsCreate = "goals:create";
        public const string GoalsEdit = "goals:edit";
        public const string GoalsDelete = "goals:delete";

        public const string KraView = "kra:view";
        public const string KraCreate = "kra:create";
        public const string KraEdit = "kra:edit";
        public const string KraDelete = "kra:delete";

        public const string KpiView = "kpi:view";
        public const string KpiCreate = "kpi:create";
        public const string KpiEdit = "kpi:edit";
        public const string KpiDelete = "kpi:delete";
        public const string KpiOverride = "kpi:override";

        public const string TargetsView = "targets:view";
        public const string TargetsEdit = "targets:edit";

        public const string ActualsView = "actuals:view";
        public const string ActualsEnter = "actuals:enter";
        public const string ActualsOverride = "actuals:override";

        public const string InitiativesView = "initiatives:view";
        public const string InitiativesCreate = "initiatives:create";
        public const string InitiativesEdit = "initiatives:edit";
        public const string InitiativesDelete = "initiatives:delete";

        // FR-403/FR-404/FR-405 are split out from InitiativesEdit so an owner can be granted
        // progress reporting without also being granted structural editing rights.
        public const string InitiativesUpdateProgress = "initiatives:progress.update";
        public const string InitiativesManageMilestones = "initiatives:milestones.manage";
        public const string InitiativesManageDependencies = "initiatives:dependencies.manage";
        public const string InitiativesUpdateExpenditure = "initiatives:expenditure.update";
        public const string InitiativesClose = "initiatives:close";

        public const string DashboardView = "dashboard:view";
        public const string DashboardConfigure = "dashboard:configure";

        // Corporate-wide performance monitoring. A division-scoped user never receives this,
        // so the centralized monitoring page and its API fall back to their own division.
        public const string PerformanceMonitorView = "performance:monitor.view";

        public const string ReportsView = "reports:view";
        public const string ReportsExport = "reports:export";

        public const string DataUpload = "data:upload";
        public const string DataValidate = "data:validate";

        public const string DivisionView = "divisions:view";
        public const string DivisionManage = "divisions:manage";
        public const string DivisionPlan = "divisions:plan";
        public const string DivisionActualsEnter = "divisions:actuals.enter";
        public const string DivisionReportsView = "divisions:reports.view";

        public const string AdminUsersManage = "admin:users.manage";
        public const string AdminRolesManage = "admin:roles.manage";
        public const string AdminConfigManage = "admin:config.manage";
        public const string AdminAuditView = "admin:audit.view";

        // FR-607: RAG thresholds are an administrator/Power User setting, so they get their own
        // permission rather than reusing AdminConfigManage (which Power Users deliberately lack).
        public const string AdminRagThresholdsManage = "admin:rag.thresholds.manage";

        // ── Access Control Management page (dynamic RBAC) ──────────────────────
        // Reading the management page (summary, catalogs, effective access, audit) requires
        // only the view right; the three manage rights represent the distinct capabilities
        // (§12 of the access-control workstream): group/membership/group-right administration,
        // the rights catalog, and direct user-level grants&denies.
        public const string AdminAccessView = "admin:access.view";
        public const string AdminAccessGroupsManage = "admin:access.groups.manage";
        public const string AdminRightsManage = "admin:access.rights.manage";
        public const string AdminUserRightsManage = "admin:access.user-rights.manage";

        public const string PermissionClaimType = "permission";

        /// <summary>
        /// Catalog-key format for programmatically created rights (the rights catalog on the
        /// Access Control Management page): "module:resource[.action]", lowercase and dotted.
        /// Seeded catalog keys all satisfy this; it guards new entries created at runtime.
        /// </summary>
        public const string PermissionKeyPattern = "^[a-z0-9]+:[a-z0-9.]+$";

        public static readonly IReadOnlyList<PermissionDefinition> All = new[]
        {
            new PermissionDefinition(OrganizationView, "Organization", "View organization profile"),
            new PermissionDefinition(OrganizationEdit, "Organization", "Edit organization profile"),

            new PermissionDefinition(ScorecardView, "Scorecard", "View scorecards"),
            new PermissionDefinition(ScorecardCreate, "Scorecard", "Create scorecards"),
            new PermissionDefinition(ScorecardEdit, "Scorecard", "Edit scorecards"),
            new PermissionDefinition(ScorecardClose, "Scorecard", "Close fiscal-year scorecards"),
            new PermissionDefinition(ScorecardClone, "Scorecard", "Clone scorecards"),
            new PermissionDefinition(ScorecardDelete, "Scorecard", "Delete scorecards"),

            new PermissionDefinition(GoalsView, "Goals", "View goals"),
            new PermissionDefinition(GoalsCreate, "Goals", "Create goals"),
            new PermissionDefinition(GoalsEdit, "Goals", "Edit goals"),
            new PermissionDefinition(GoalsDelete, "Goals", "Delete goals"),

            new PermissionDefinition(KraView, "KRAs", "View KRAs"),
            new PermissionDefinition(KraCreate, "KRAs", "Create KRAs"),
            new PermissionDefinition(KraEdit, "KRAs", "Edit KRAs"),
            new PermissionDefinition(KraDelete, "KRAs", "Delete KRAs"),

            new PermissionDefinition(KpiView, "KPIs", "View KPIs"),
            new PermissionDefinition(KpiCreate, "KPIs", "Create KPIs"),
            new PermissionDefinition(KpiEdit, "KPIs", "Edit KPIs"),
            new PermissionDefinition(KpiDelete, "KPIs", "Delete KPIs"),
            new PermissionDefinition(KpiOverride, "KPIs", "Override locked-period KPI values"),

            new PermissionDefinition(TargetsView, "Targets", "View KPI targets"),
            new PermissionDefinition(TargetsEdit, "Targets", "Edit KPI targets"),

            new PermissionDefinition(ActualsView, "Actuals", "View actual values"),
            new PermissionDefinition(ActualsEnter, "Actuals", "Enter actual values"),
            new PermissionDefinition(ActualsOverride, "Actuals", "Override actual values after cutoff"),

            new PermissionDefinition(InitiativesView, "Initiatives", "View initiatives"),
            new PermissionDefinition(InitiativesCreate, "Initiatives", "Create initiatives"),
            new PermissionDefinition(InitiativesEdit, "Initiatives", "Edit initiatives"),
            new PermissionDefinition(InitiativesDelete, "Initiatives", "Delete initiatives"),
            new PermissionDefinition(InitiativesUpdateProgress, "Initiatives", "Update initiative milestone progress"),
            new PermissionDefinition(InitiativesManageMilestones, "Initiatives", "Add, edit and remove initiative milestones"),
            new PermissionDefinition(InitiativesManageDependencies, "Initiatives", "Manage initiative dependencies"),
            new PermissionDefinition(InitiativesUpdateExpenditure, "Initiatives", "Update initiative actual expenditure"),
            new PermissionDefinition(InitiativesClose, "Initiatives", "Close initiatives and record closure information"),

            new PermissionDefinition(DashboardView, "Dashboard", "View dashboards"),
            new PermissionDefinition(DashboardConfigure, "Dashboard", "Configure custom dashboards and widgets"),
            new PermissionDefinition(PerformanceMonitorView, "Dashboard", "View corporate-wide performance monitoring"),

            new PermissionDefinition(ReportsView, "Reports", "View reports"),
            new PermissionDefinition(ReportsExport, "Reports", "Export reports (PDF/Excel/CSV)"),

            new PermissionDefinition(DataUpload, "Data Management", "Bulk upload data"),
            new PermissionDefinition(DataValidate, "Data Management", "Validate and commit bulk uploads"),

            new PermissionDefinition(DivisionView, "Divisions", "View divisions and division plans"),
            new PermissionDefinition(DivisionManage, "Divisions", "Create and manage divisions"),
            new PermissionDefinition(DivisionPlan, "Divisions", "Plan division goals, contributions and KPIs"),
            new PermissionDefinition(DivisionActualsEnter, "Divisions", "Enter division actual performance values"),
            new PermissionDefinition(DivisionReportsView, "Divisions", "View division performance reports"),

            new PermissionDefinition(AdminUsersManage, "Admin", "Manage users"),
            new PermissionDefinition(AdminRolesManage, "Admin", "Manage roles and permissions"),
            new PermissionDefinition(AdminConfigManage, "Admin", "Manage system configuration"),
            new PermissionDefinition(AdminAuditView, "Admin", "View audit logs"),
            new PermissionDefinition(AdminRagThresholdsManage, "Admin", "Manage RAG status thresholds"),
            new PermissionDefinition(AdminAccessView, "Admin", "View the Access Control Management page"),
            new PermissionDefinition(AdminAccessGroupsManage, "Admin", "Manage access groups, memberships and group rights"),
            new PermissionDefinition(AdminRightsManage, "Admin", "Manage the rights (permission) catalog"),
            new PermissionDefinition(AdminUserRightsManage, "Admin", "Manage direct user grants and denies"),
        };
    }
}