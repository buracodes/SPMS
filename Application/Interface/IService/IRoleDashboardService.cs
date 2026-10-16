using Application.DTOS.Dashboard;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IService;

/// <summary>Global dashboard filter. Defaults follow the FRD: YTD, current fiscal year, first active scorecard.</summary>
public sealed class DashboardFilterQuery
{
    public Guid? FiscalYearId { get; set; }
    public Guid? ScorecardId { get; set; }

    /// <summary>YTD | Quarter | Month | Year. Anything else is treated as YTD.</summary>
    public string? PeriodType { get; set; }

    public int? Quarter { get; set; }
    public int? FromMonth { get; set; }
    public int? ToMonth { get; set; }
}

/// <summary>
/// Role driven dashboard. The shell, widgets, actions and data scope all come from the
/// authenticated user's roles and permissions - the server never returns records the
/// caller is not allowed to see.
/// </summary>
public interface IRoleDashboardService
{
    /// <summary>Role profile, permitted widgets, permitted actions and the available filter options.</summary>
    Task<DashboardRoleContextDto> GetContextAsync(DashboardFilterQuery? filter, CancellationToken cancellationToken = default);

    /// <summary>Payload for every widget the caller's role and permissions allow, under one shared filter.</summary>
    Task<DashboardWidgetBundleDto> GetWidgetsAsync(DashboardFilterQuery? filter, CancellationToken cancellationToken = default);

    /// <summary>Goal to KRA to KPI drill-down, scoped to the caller's data scope.</summary>
    Task<GoalDrilldownDto> GetGoalDrilldownAsync(Guid goalId, DashboardFilterQuery? filter, CancellationToken cancellationToken = default);
}
