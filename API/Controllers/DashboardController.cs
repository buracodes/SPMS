using Application.Dashboard;
using Application.DTOS.Dashboard;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Asp.Versioning;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class DashboardController : ControllerBase
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IGenericRepository<Domain.Entities.Goal> goalRepository;
    private readonly IKraRepository kraRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IGenericRepository<Domain.Entities.Initiative> initiativeRepository;
    private readonly IUserRepository userRepository;
    private readonly IGenericRepository<Domain.Entities.Role> roleRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IDivisionRepository divisionRepository;
    private readonly IAuditLogEntryRepository auditLogRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IPeriodLockConfigRepository periodLockConfigRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IRoleDashboardService roleDashboardService;

    public DashboardController(
        IScorecardRepository _scorecardRepository,
        IGenericRepository<Domain.Entities.Goal> _goalRepository,
        IKraRepository _kraRepository,
        IKpiRepository _kpiRepository,
        IGenericRepository<Domain.Entities.Initiative> _initiativeRepository,
        IUserRepository _userRepository,
        IGenericRepository<Domain.Entities.Role> _roleRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IDivisionRepository _divisionRepository,
        IAuditLogEntryRepository _auditLogRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IPeriodLockConfigRepository _periodLockConfigRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IRoleDashboardService _roleDashboardService)
    {
        scorecardRepository = _scorecardRepository;
        goalRepository = _goalRepository;
        kraRepository = _kraRepository;
        kpiRepository = _kpiRepository;
        initiativeRepository = _initiativeRepository;
        userRepository = _userRepository;
        roleRepository = _roleRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        divisionRepository = _divisionRepository;
        auditLogRepository = _auditLogRepository;
        kpiTargetRepository = _kpiTargetRepository;
        periodLockConfigRepository = _periodLockConfigRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        fiscalYearRepository = _fiscalYearRepository;
        roleDashboardService = _roleDashboardService;
    }

    [HttpGet("summary")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<DashboardSummaryDto>>> GetSummary()
    {
        var scorecards = await scorecardRepository.GetAll();
        var goals = await goalRepository.GetAll();
        var kras = await kraRepository.GetAll();
        var kpis = await kpiRepository.GetAll();
        var initiatives = await initiativeRepository.GetAll();

        return Ok(new Response<DashboardSummaryDto>
        {
            Success = true,
            Message = "Dashboard summary retrieved successfully.",
            Data = new DashboardSummaryDto
            {
                TotalScorecards = scorecards.Count,
                ActiveScorecards = scorecards.Count(s => s.ScorecardStatus == ScorecardStatus.Active),
                ClosedScorecards = scorecards.Count(s => s.ScorecardStatus == ScorecardStatus.Closed),
                TotalGoals = goals.Count,
                TotalKras = kras.Count,
                TotalKpis = kpis.Count,
                TotalInitiatives = initiatives.Count,
                ActiveInitiatives = initiatives.Count(i => i.Status == InitiativeStatus.InProgress),
                CompletedInitiatives = initiatives.Count(i => i.Status == InitiativeStatus.Completed),
            }
        });
    }

    [HttpGet("overview")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<DashboardOverviewDto>>> GetOverview()
    {
        var role = ResolvePrimaryRole();

        var scorecards = await scorecardRepository.GetAll();
        var goals = await goalRepository.GetAll();
        var kras = await kraRepository.GetAll();
        var kpis = await kpiRepository.GetAll();
        var initiatives = await initiativeRepository.GetAll();

        var activeScorecards = scorecards.Count(s => s.ScorecardStatus == ScorecardStatus.Active);
        var closedScorecards = scorecards.Count(s => s.ScorecardStatus == ScorecardStatus.Closed);
        var plannedInitiatives = initiatives.Count(i => i.Status == InitiativeStatus.NotStarted);
        var inProgressInitiatives = initiatives.Count(i => i.Status == InitiativeStatus.InProgress);
        var completedInitiatives = initiatives.Count(i => i.Status == InitiativeStatus.Completed);

        var tiles = new List<DashboardTileDto>();
        var quickLinks = new List<DashboardQuickLinkDto>();
        string insight;

        switch (role)
        {
            case Roles.Admin:
                var users = await userRepository.GetAll();
                var roles = await roleRepository.GetAll();
                tiles.Add(Tile("scorecards", "Scorecards", scorecards.Count.ToString(), $"{activeScorecards} active · {closedScorecards} closed", "primary"));
                tiles.Add(Tile("users", "Users", users.Count.ToString(), $"{roles.Count} roles defined", "success"));
                tiles.Add(Tile("goals", "Goals", goals.Count.ToString(), null, "info"));
                tiles.Add(Tile("kras", "KRAs", kras.Count.ToString(), null, "warning"));
                tiles.Add(Tile("kpis", "KPIs", kpis.Count.ToString(), null, "purple"));
                tiles.Add(Tile("initiatives", "Initiatives", initiatives.Count.ToString(), $"{inProgressInitiatives} in progress · {completedInitiatives} completed", "primary"));
                quickLinks.AddRange(new[]
                {
                    QuickLink("Manage Users", "admin/users"),
                    QuickLink("Manage Roles", "admin/roles"),
                    QuickLink("Fiscal Years", "admin/fiscal-years"),
                    QuickLink("Data Locking", "data-locking"),
                });
                insight = "Full administrative access across the strategic planning system.";
                break;

            case Roles.PowerUser:
                tiles.Add(Tile("scorecards", "Scorecards", scorecards.Count.ToString(), $"{activeScorecards} active · {closedScorecards} closed", "primary"));
                tiles.Add(Tile("goals", "Goals", goals.Count.ToString(), null, "info"));
                tiles.Add(Tile("kras", "KRAs", kras.Count.ToString(), null, "warning"));
                tiles.Add(Tile("kpis", "KPIs", kpis.Count.ToString(), null, "purple"));
                tiles.Add(Tile("initiatives", "Initiatives", initiatives.Count.ToString(), $"{inProgressInitiatives} in progress · {completedInitiatives} completed", "primary"));
                quickLinks.AddRange(new[]
                {
                    QuickLink("Scorecard Management", "scorecards/manage"),
                    QuickLink("Data Locking", "data-locking"),
                    QuickLink("Data Management", "data-management"),
                    QuickLink("Initiatives", "initiatives"),
                });
                insight = "Configure strategy, manage data, and track initiatives.";
                break;

            case Roles.Executive:
                var divisions = await divisionRepository.GetActiveAsync();
                tiles.Add(Tile("scorecards", "Scorecards", scorecards.Count.ToString(), $"{activeScorecards} active · {closedScorecards} closed", "success"));
                tiles.Add(Tile("initiatives", "Initiatives", completedInitiatives.ToString(), $"{inProgressInitiatives} in progress · {plannedInitiatives} planned", "primary"));
                tiles.Add(Tile("divisions", "Divisions", divisions.Count.ToString(), "organizational units", "info"));
                tiles.Add(Tile("kpis", "KPIs", kpis.Count.ToString(), "performance indicators tracked", "warning"));
                tiles.Add(Tile("goals", "Goals", goals.Count.ToString(), "organizational ambitions", "purple"));
                quickLinks.AddRange(new[]
                {
                    QuickLink("Scorecard Performance", "scorecards/performance"),
                    QuickLink("Reports", "reports"),
                    QuickLink("Initiatives", "initiatives"),
                    QuickLink("Data Locking", "data-locking"),
                });
                insight = "Monitor organizational performance and strategic execution.";
                break;

            case Roles.DataInputUser:
                var actuals = await kpiActualValueRepository.GetAll();
                tiles.Add(Tile("scorecards", "Active Scorecards", activeScorecards.ToString(), $"{scorecards.Count} total scorecards", "success"));
                tiles.Add(Tile("kpis", "KPIs", kpis.Count.ToString(), "indicators for actual entry", "info"));
                tiles.Add(Tile("actuals", "Actuals Entered", actuals.Count.ToString(), "values recorded to date", "primary"));
                tiles.Add(Tile("initiatives", "Initiatives", initiatives.Count.ToString(), "initiatives to observe", "warning"));
                quickLinks.AddRange(new[]
                {
                    QuickLink("Enter Actuals", "actuals-data"),
                    QuickLink("Data Management", "data-management"),
                    QuickLink("Scorecard Performance", "scorecards/performance"),
                });
                insight = "Enter and validate actual performance data before period cutoffs.";
                break;

            default:
                tiles.Add(Tile("scorecards", "Scorecards", scorecards.Count.ToString(), $"{activeScorecards} active · {closedScorecards} closed", "primary"));
                tiles.Add(Tile("goals", "Goals", goals.Count.ToString(), null, "info"));
                tiles.Add(Tile("kras", "KRAs", kras.Count.ToString(), null, "warning"));
                tiles.Add(Tile("kpis", "KPIs", kpis.Count.ToString(), null, "purple"));
                tiles.Add(Tile("initiatives", "Initiatives", initiatives.Count.ToString(), $"{inProgressInitiatives} in progress · {completedInitiatives} completed", "primary"));
                quickLinks.AddRange(new[]
                {
                    QuickLink("Scorecard Hierarchy", "scorecards/hierarchy"),
                    QuickLink("Reports", "reports"),
                    QuickLink("Initiatives", "initiatives"),
                });
                insight = "Read-only view across the strategic planning system.";
                break;
        }

        return Ok(new Response<DashboardOverviewDto>
        {
            Success = true,
            Message = "Role-based dashboard retrieved successfully.",
            Data = new DashboardOverviewDto
            {
                Role = role,
                Insight = insight,
                Tiles = tiles,
                QuickLinks = quickLinks,
            }
        });
    }

    [HttpGet("activity")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<List<DashboardActivityDto>>>> GetActivity()
    {
        var entries = await auditLogRepository.GetRecentAsync(8);
        var latest = entries
            .Select(e => new DashboardActivityDto
            {
                EntityType = e.EntityType,
                Action = e.Action,
                Description = BuildActivityDescription(e),
                User = string.IsNullOrWhiteSpace(e.PerformedByName) ? "System" : e.PerformedByName!,
                Timestamp = e.PerformedAt
            })
            .ToList();

        return Ok(new Response<List<DashboardActivityDto>>
        {
            Success = true,
            Message = "Recent activity retrieved successfully.",
            Data = latest
        });
    }

    [HttpGet("pending-actions")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<List<DashboardPendingActionDto>>>> GetPendingActions()
    {
        var actions = new List<DashboardPendingActionDto>();

        var kpis = await kpiRepository.GetAll();
        var targets = await kpiTargetRepository.GetAll();
        var kpisWithoutTargets = kpis.Count(k => !targets.Any(t => t.KpiId == k.Id));
        if (kpisWithoutTargets > 0)
        {
            actions.Add(PendingAction(
                "missing-targets",
                "KPI target configuration",
                "Annual, quarterly and monthly targets not yet configured.",
                kpisWithoutTargets,
                "High",
                "/kpis"));
        }

        var fiscalPeriods = await fiscalPeriodRepository.GetAll();
        var lockedConfigs = await periodLockConfigRepository.GetAll();
        var openPeriods = fiscalPeriods
            .Where(p => p.EndDate >= DateTime.UtcNow)
            .Select(p => p.Id)
            .ToHashSet();
        var periodsWithoutActuals = openPeriods
            .Where(periodId => !lockedConfigs.Any(
                l => l.FiscalPeriodId == periodId && l.LockedAtUtc.HasValue))
            .Count();
        if (periodsWithoutActuals > 0)
        {
            actions.Add(PendingAction(
                "pending-submissions",
                "Actual data submissions pending",
                "Open fiscal periods awaiting manual actual entries.",
                periodsWithoutActuals,
                "High",
                "/data-entry"));
        }

        var daysToCutoff = DateTime.UtcNow.AddDays(3);
        var locksApproaching = lockedConfigs
            .Where(l => !l.LockedAtUtc.HasValue)
            .Join(fiscalPeriods, l => l.FiscalPeriodId, p => p.Id, (l, p) => p.EndDate)
            .Count(endDate => endDate <= daysToCutoff && endDate >= DateTime.UtcNow);
        if (locksApproaching > 0)
        {
            actions.Add(PendingAction(
                "lock-approaching",
                "Period lock approaching",
                "Entry windows closing within the next 3 days.",
                locksApproaching,
                "Medium",
                "/data-locking"));
        }

        return Ok(new Response<List<DashboardPendingActionDto>>
        {
            Success = true,
            Message = "Pending actions retrieved successfully.",
            Data = actions
        });
    }

    // ------------------------------------------------------------------
    // Role aware dashboard shell (FRD): one payload per role, filtered server side.
    // ------------------------------------------------------------------

    /// <summary>
    /// Everything the shell needs to render: resolved role, the applied global filter
    /// (fiscal year, scorecard, period), the available filter options, the widget
    /// registry for this role and the permission checked quick actions.
    /// </summary>
    [HttpGet("context")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<DashboardRoleContextDto>>> GetContext(
        [FromQuery] Guid? fiscalYearId,
        [FromQuery] Guid? scorecardId,
        [FromQuery] string? periodType,
        [FromQuery] int? quarter,
        [FromQuery] int? fromMonth,
        [FromQuery] int? toMonth,
        CancellationToken cancellationToken)
    {
        var data = await roleDashboardService.GetContextAsync(
            Filter(fiscalYearId, scorecardId, periodType, quarter, fromMonth, toMonth),
            cancellationToken);

        return Ok(new Response<DashboardRoleContextDto>
        {
            Success = true,
            Message = "Dashboard context retrieved successfully.",
            Data = data
        });
    }

    /// <summary>Widget payloads for the signed-in role, limited to the filters applied above.</summary>
    [HttpGet("widgets")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<DashboardWidgetBundleDto>>> GetWidgets(
        [FromQuery] Guid? fiscalYearId,
        [FromQuery] Guid? scorecardId,
        [FromQuery] string? periodType,
        [FromQuery] int? quarter,
        [FromQuery] int? fromMonth,
        [FromQuery] int? toMonth,
        CancellationToken cancellationToken)
    {
        var data = await roleDashboardService.GetWidgetsAsync(
            Filter(fiscalYearId, scorecardId, periodType, quarter, fromMonth, toMonth),
            cancellationToken);

        return Ok(new Response<DashboardWidgetBundleDto>
        {
            Success = true,
            Message = "Dashboard widgets retrieved successfully.",
            Data = data
        });
    }

    /// <summary>Goal drill-down (KRAs and KPIs) for the "drill down" goal widget.</summary>
    [HttpGet("goals/{goalId:guid}/kras")]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<GoalDrilldownDto>>> GetGoalKras(
        Guid goalId,
        [FromQuery] Guid? fiscalYearId,
        [FromQuery] Guid? scorecardId,
        [FromQuery] string? periodType,
        [FromQuery] int? quarter,
        [FromQuery] int? fromMonth,
        [FromQuery] int? toMonth,
        CancellationToken cancellationToken)
    {
        var data = await roleDashboardService.GetGoalDrilldownAsync(
            goalId,
            Filter(fiscalYearId, scorecardId, periodType, quarter, fromMonth, toMonth),
            cancellationToken);

        return Ok(new Response<GoalDrilldownDto>
        {
            Success = true,
            Message = "Goal drill-down retrieved successfully.",
            Data = data
        });
    }

    private static DashboardFilterQuery Filter(
        Guid? fiscalYearId,
        Guid? scorecardId,
        string? periodType,
        int? quarter,
        int? fromMonth,
        int? toMonth)
        => new()
        {
            FiscalYearId = fiscalYearId,
            ScorecardId = scorecardId,
            PeriodType = periodType,
            Quarter = quarter,
            FromMonth = fromMonth,
            ToMonth = toMonth
        };

    private static string BuildActivityDescription(Domain.Entities.AuditLogEntry entry)
        => AuditDescription.For(entry.EntityType, entry.Action);

    private static DashboardPendingActionDto PendingAction(string key, string title, string description, int count, string priority, string route)
        => new() { Key = key, Title = title, Description = description, Count = count, Priority = priority, Route = route };

    private string ResolvePrimaryRole()
    {
        string? role = null;
        foreach (var claim in User.Claims)
        {
            if (claim.Type.Contains("role", StringComparison.OrdinalIgnoreCase))
            {
                role = claim.Value;
                break;
            }
        }

        return role is not null && Roles.All.Contains(role) ? role : Roles.ViewOnly;
    }

    private static DashboardTileDto Tile(string key, string label, string value, string? detail, string tone)
        => new() { Key = key, Label = label, Value = value, Detail = detail, Tone = tone };

    private static DashboardQuickLinkDto QuickLink(string label, string route)
        => new() { Label = label, Route = route };
}

public class DashboardSummaryDto
{
    public int TotalScorecards { get; set; }
    public int ActiveScorecards { get; set; }
    public int ClosedScorecards { get; set; }
    public int TotalGoals { get; set; }
    public int TotalKras { get; set; }
    public int TotalKpis { get; set; }
    public int TotalInitiatives { get; set; }
    public int ActiveInitiatives { get; set; }
    public int CompletedInitiatives { get; set; }
}

public class DashboardOverviewDto
{
    public string Role { get; set; } = string.Empty;
    public string Insight { get; set; } = string.Empty;
    public List<DashboardTileDto> Tiles { get; set; } = new();
    public List<DashboardQuickLinkDto> QuickLinks { get; set; } = new();
}

public class DashboardTileDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string Tone { get; set; } = "primary";
}

public class DashboardQuickLinkDto
{
    public string Label { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

public class DashboardActivityDto
{
    public string EntityType { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string Description { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class DashboardPendingActionDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}