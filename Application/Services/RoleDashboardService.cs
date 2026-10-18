using Application.DTOS.Dashboard;
using Application.Dashboard;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Domain.Calculation;
using System.Globalization;

namespace Application.Services;

/// <summary>
/// Role driven dashboard. A single shared shell is fed by this service: it resolves the
/// caller's role from the authenticated claims, decides which widgets and actions the role
/// may receive, and applies the data scope (organization wide or assigned only) to every
/// query before any payload is projected. Scores always come from the existing SPMS
/// calculation engine (<see cref="IScorePeriodCalculationService"/>) - never recalculated here.
/// </summary>
public sealed partial class RoleDashboardService : IRoleDashboardService
{
    private readonly ICurrentUserService currentUserService;
    private readonly IScorePeriodCalculationService scorePeriodCalculationService;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionScoreSnapshotRepository divisionScoreSnapshotRepository;
    private readonly IDivisionPeriodCalculationService divisionPeriodCalculationService;
    private readonly IPeriodLockConfigRepository periodLockConfigRepository;
    private readonly IPeriodLockStatusService periodLockStatusService;
    private readonly IAuditLogEntryRepository auditLogRepository;
    private readonly IUserRepository userRepository;
    private readonly IGenericRepository<Role> roleRepository;
    private readonly IInitiativeProgressCalculator initiativeProgressCalculator;
    private readonly IBusinessClock businessClock;
    private readonly IRagThresholdService ragThresholdService;

    public RoleDashboardService(
        ICurrentUserService currentUserService,
        IScorePeriodCalculationService scorePeriodCalculationService,
        IScorecardRepository scorecardRepository,
        IFiscalYearRepository fiscalYearRepository,
        IFiscalPeriodRepository fiscalPeriodRepository,
        IGoalRepository goalRepository,
        IKpiRepository kpiRepository,
        IKpiActualValueRepository kpiActualValueRepository,
        IKpiTargetRepository kpiTargetRepository,
        IInitiativeRepository initiativeRepository,
        IDivisionPlanRepository divisionPlanRepository,
        IDivisionScoreSnapshotRepository divisionScoreSnapshotRepository,
        IDivisionPeriodCalculationService divisionPeriodCalculationService,
        IPeriodLockConfigRepository periodLockConfigRepository,
        IPeriodLockStatusService periodLockStatusService,
        IAuditLogEntryRepository auditLogRepository,
        IUserRepository userRepository,
        IGenericRepository<Role> roleRepository,
        IInitiativeProgressCalculator initiativeProgressCalculator,
        IBusinessClock businessClock,
        IRagThresholdService ragThresholdService)
    {
        this.currentUserService = currentUserService;
        this.scorePeriodCalculationService = scorePeriodCalculationService;
        this.scorecardRepository = scorecardRepository;
        this.fiscalYearRepository = fiscalYearRepository;
        this.fiscalPeriodRepository = fiscalPeriodRepository;
        this.goalRepository = goalRepository;
        this.kpiRepository = kpiRepository;
        this.kpiActualValueRepository = kpiActualValueRepository;
        this.kpiTargetRepository = kpiTargetRepository;
        this.initiativeRepository = initiativeRepository;
        this.divisionPlanRepository = divisionPlanRepository;
        this.divisionScoreSnapshotRepository = divisionScoreSnapshotRepository;
        this.divisionPeriodCalculationService = divisionPeriodCalculationService;
        this.periodLockConfigRepository = periodLockConfigRepository;
        this.periodLockStatusService = periodLockStatusService;
        this.auditLogRepository = auditLogRepository;
        this.userRepository = userRepository;
        this.roleRepository = roleRepository;
        this.initiativeProgressCalculator = initiativeProgressCalculator;
        this.businessClock = businessClock;
        this.ragThresholdService = ragThresholdService;
    }

    private const int MaxScorecardsForComparison = 12;
    private const int TopBottomKpiCount = 5;
    private const int ExceptionKpiCount = 8;
    private const int MaxAuditRows = 10;
    private const int ConsecutiveRedMonthsAlertThreshold = 3;
    private const int MilestoneDeadlineWindowDays = 7;

    // ------------------------------------------------------------------ context

    public async Task<DashboardRoleContextDto> GetContextAsync(
        DashboardFilterQuery? filter, CancellationToken cancellationToken = default)
    {
        var profile = ResolveProfile();
        var permissions = currentUserService.Permissions;
        var load = await LoadAsync(profile, permissions, filter, cancellationToken);

        return new DashboardRoleContextDto
        {
            Role = profile.Role,
            DisplayName = profile.DisplayName,
            Purpose = profile.Purpose,
            Insight = profile.Insight,
            DataScope = profile.DataScope.ToString(),
            ReadOnly = profile.IsReadOnly,
            Filter = BuildFilterDto(load),
            Widgets = load.Widgets.Select(ToWidgetDto).ToList(),
            QuickActions = DashboardRoleCatalog
                .ResolveActions(profile, permissions)
                .Select(a => new DashboardQuickActionDto
                {
                    Key = a.Key,
                    Label = a.Label,
                    Route = a.Route,
                    Icon = a.Icon,
                    Emphasis = a.Emphasis
                })
                .ToList(),
            FiscalYears = await BuildFiscalYearOptionsAsync(load, cancellationToken),
            Scorecards = BuildScorecardOptions(load),
            Periods = BuildPeriodOptions(load)
        };
    }

    // ------------------------------------------------------------------ widgets

    public async Task<DashboardWidgetBundleDto> GetWidgetsAsync(
        DashboardFilterQuery? filter, CancellationToken cancellationToken = default)
    {
        var profile = ResolveProfile();
        var permissions = currentUserService.Permissions;
        var load = await LoadAsync(profile, permissions, filter, cancellationToken);

        var bundle = new DashboardWidgetBundleDto
        {
            Role = profile.Role,
            DataScope = profile.DataScope.ToString(),
            Filter = BuildFilterDto(load),
            Widgets = load.Widgets.Select(ToWidgetDto).ToList()
        };

        if (!load.Widgets.Any())
        {
            return bundle;
        }

        bundle.Summary = load.Has(DashboardRoleCatalog.WidgetKpiSummary) ? await BuildSummaryAsync(load, cancellationToken) : null;
        bundle.OverallScore = load.Has(DashboardRoleCatalog.WidgetOverallScore) ? BuildOverallScore(load) : null;
        bundle.RagDistribution = load.Has(DashboardRoleCatalog.WidgetRagDistribution) ? BuildRagDistribution(load) : null;
        bundle.PerformanceTrend = load.Has(DashboardRoleCatalog.WidgetPerformanceTrend) ? BuildPerformanceTrend(load) : null;
        bundle.ScorecardPerformance = load.Has(DashboardRoleCatalog.WidgetScorecardPerformance) ? await BuildScorecardPerformanceAsync(load, cancellationToken) : null;
        bundle.ScorecardHealth = load.Has(DashboardRoleCatalog.WidgetScorecardHealth) ? await BuildScorecardHealthAsync(load, cancellationToken) : null;
        bundle.TargetVsActual = load.Has(DashboardRoleCatalog.WidgetTargetVsActual) ? BuildTargetVsActual(load) : null;
        bundle.GoalHealth = load.Has(DashboardRoleCatalog.WidgetGoalHealth) ? BuildGoalTable(load, drillDown: true) : null;
        bundle.GoalPerformance = load.Has(DashboardRoleCatalog.WidgetGoalPerformance) ? BuildGoalTable(load, drillDown: false) : null;
        bundle.GoalPerformanceChart = load.Has(DashboardRoleCatalog.WidgetGoalPerformanceChart) ? BuildGoalPerformanceChart(load) : null;
        bundle.TopBottomKpis = load.Has(DashboardRoleCatalog.WidgetTopBottomKpis) ? await BuildTopBottomKpisAsync(load, cancellationToken) : null;
        bundle.KpiExceptions = load.Has(DashboardRoleCatalog.WidgetKpiExceptions) ? await BuildKpiExceptionsAsync(load, cancellationToken) : null;
        bundle.StrategicAlignment = load.Has(DashboardRoleCatalog.WidgetStrategicAlignment) ? await BuildStrategicAlignmentAsync(load, cancellationToken) : null;
        bundle.InitiativeStatus = load.Has(DashboardRoleCatalog.WidgetInitiativeStatus) ? await BuildInitiativeStatusAsync(load, cancellationToken) : null;
        bundle.InitiativeDeliveryChart = load.Has(DashboardRoleCatalog.WidgetInitiativeDeliveryChart) ? BuildInitiativeDeliveryChart(load) : null;
        bundle.MilestoneTrend = load.Has(DashboardRoleCatalog.WidgetMilestoneTrend) ? BuildMilestoneTrend(load) : null;
        bundle.BudgetSummary = load.Has(DashboardRoleCatalog.WidgetBudgetSummary) ? BuildBudgetSummary(load) : null;
        bundle.BudgetAllocationChart = load.Has(DashboardRoleCatalog.WidgetBudgetAllocationChart) ? BuildBudgetAllocationChart(load) : null;

        // One calculation feeds both the division table and the division chart.
        if (load.Has(DashboardRoleCatalog.WidgetDivisionComparison)
            || load.Has(DashboardRoleCatalog.WidgetDivisionPerformanceChart))
        {
            var divisions = await DivisionScoresAsync(load, cancellationToken);
            bundle.DivisionComparison = load.Has(DashboardRoleCatalog.WidgetDivisionComparison)
                ? BuildDivisionComparison(load, divisions)
                : null;
            bundle.DivisionPerformanceChart = load.Has(DashboardRoleCatalog.WidgetDivisionPerformanceChart)
                ? BuildDivisionPerformanceChart(load, divisions)
                : null;
        }

        bundle.AssignedKpis = load.Has(DashboardRoleCatalog.WidgetAssignedKpis) ? await BuildAssignedKpisAsync(load, cancellationToken) : null;
        bundle.DataEntryStatus = load.Has(DashboardRoleCatalog.WidgetDataEntryStatus) ? await BuildDataEntryStatusAsync(load, cancellationToken) : null;
        bundle.PeriodLocks = load.Has(DashboardRoleCatalog.WidgetPeriodLocks) ? BuildPeriodLocks(load) : null;
        bundle.SystemStats = load.Has(DashboardRoleCatalog.WidgetSystemStats) ? await BuildSystemStatsAsync(cancellationToken) : null;
        bundle.AuditActivity = load.Has(DashboardRoleCatalog.WidgetAuditActivity) ? await BuildAuditActivityAsync(cancellationToken) : null;
        bundle.ReportsSummary = load.Has(DashboardRoleCatalog.WidgetReportsSummary) ? BuildReportsSummary(load) : null;
        bundle.Alerts = load.Has(DashboardRoleCatalog.WidgetAlerts) ? await BuildAlertsAsync(load, cancellationToken) : null;

        return bundle;
    }

    // ------------------------------------------------------------------ drill-down

    public async Task<GoalDrilldownDto> GetGoalDrilldownAsync(
        Guid goalId, DashboardFilterQuery? filter, CancellationToken cancellationToken = default)
    {
        var profile = ResolveProfile();
        var permissions = currentUserService.Permissions;

        if (!permissions.Contains(Permissions.GoalsView))
        {
            throw new Exceptions.UnauthorizedException("You do not have permission to view goals.");
        }

        var load = await LoadAsync(profile, permissions, filter, cancellationToken);
        var goal = load.Goals.FirstOrDefault(g => g.Id == goalId);

        if (goal is null)
        {
            // Assigned-only users must not learn about goals outside their scope.
            return new GoalDrilldownDto { GoalId = goalId, GoalName = string.Empty };
        }

        return BuildGoalDrilldown(load, goal);
    }

    // ------------------------------------------------------------------ role + scope

    private DashboardRoleProfile ResolveProfile() => DashboardRoleCatalog.Resolve(currentUserService.Roles);

    /// <summary>Assigned KPI ids for a data entry user: owner or division owner.</summary>
    private bool IsAssignedToCurrentUser(Kpi kpi)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue)
        {
            return false;
        }

        return kpi.OwnerId == userId || kpi.DivisionOwnerId == userId;
    }

    // ------------------------------------------------------------------ loading

    private sealed class DashboardLoad
    {
        public required DashboardRoleProfile Profile { get; init; }
        public required IReadOnlyList<DashboardWidgetDefinition> Widgets { get; init; }
        public required IReadOnlyCollection<string> Permissions { get; init; }

        public FiscalYear? FiscalYear { get; set; }
        public IReadOnlyList<FiscalPeriod> Periods { get; set; } = Array.Empty<FiscalPeriod>();
        public IReadOnlyList<Scorecard> Scorecards { get; set; } = Array.Empty<Scorecard>();
        public Scorecard? SelectedScorecard { get; set; }
        public IReadOnlyList<Goal> Goals { get; set; } = Array.Empty<Goal>();
        public IReadOnlyList<Kpi> Kpis { get; set; } = Array.Empty<Kpi>();
        public IReadOnlyDictionary<Guid, ScorePeriodResult> PeriodResults { get; set; }
            = new Dictionary<Guid, ScorePeriodResult>();
        public ScorePeriodResult? SelectedPeriodResult { get; set; }
        public ScorePeriodResult? SelectedYearResult { get; set; }
        public IReadOnlyList<KpiActualValue> Actuals { get; set; } = Array.Empty<KpiActualValue>();
        public IReadOnlyList<KpiTarget> Targets { get; set; } = Array.Empty<KpiTarget>();

        /// <summary>Lock state per fiscal month (GetStatusesAsync returns one entry per period, in order).</summary>
        public IReadOnlyDictionary<int, PeriodLockInfo> LocksByMonth { get; set; } = new Dictionary<int, PeriodLockInfo>();
        public IReadOnlyList<Initiative> Initiatives { get; set; } = Array.Empty<Initiative>();
        public IReadOnlyDictionary<Guid, int> KpiCountByGoal { get; set; } = new Dictionary<Guid, int>();
        public string RequestedPeriodType { get; set; } = "Ytd";
        public int? RequestedQuarter { get; set; }
        public int? RequestedFromMonth { get; set; }
        public int? RequestedToMonth { get; set; }
        /// <summary>ECX business date (EAT) that "today" is evaluated against.</summary>
        public DateTime BusinessDate { get; set; } = DateTime.UtcNow.Date;
        public int CurrentFiscalMonth { get; set; }

        /// <summary>Effective RAG thresholds (FR-607) for this request; every widget bands with these.</summary>
        public RagThresholds Thresholds { get; set; } = RagThresholds.Default;

        /// <summary>Single authoritative RAG band for a score in this dashboard payload.</summary>
        public string Rag(decimal? score) => DashboardRag.FromScore(score, Thresholds);

        public bool RagIsRed(decimal? score) => DashboardRag.IsRed(score, Thresholds);

        public bool Has(string widgetKey) => Widgets.Any(w => w.Key == widgetKey);

        public bool Granted(string permission) => Permissions.Contains(permission);
    }

    private async Task<DashboardLoad> LoadAsync(
        DashboardRoleProfile profile,
        IReadOnlyCollection<string> permissions,
        DashboardFilterQuery? filter,
        CancellationToken cancellationToken)
    {
        var load = new DashboardLoad
        {
            Profile = profile,
            Widgets = DashboardRoleCatalog.ResolveWidgets(profile, permissions),
            Permissions = permissions,
            BusinessDate = businessClock.BusinessDate,
            // FR-607: load the effective thresholds once per dashboard request so every widget,
            // chart and alert bands the same score identically.
            Thresholds = await ragThresholdService.GetEffectiveAsync(cancellationToken: cancellationToken)
        };

        // ---- fiscal year (default: current open year, else the most recent)
        var fiscalYears = await fiscalYearRepository.GetAll();
        var orderedYears = fiscalYears
            .OrderByDescending(f => f.StartDate)
            .ToList();

        var fiscalYear = filter?.FiscalYearId is Guid requestedYearId
            ? orderedYears.FirstOrDefault(f => f.Id == requestedYearId)
            : orderedYears.FirstOrDefault(f => f.FiscalYearStatus == FiscalYearStatus.Open) ?? orderedYears.FirstOrDefault();

        load.FiscalYear = fiscalYear;
        if (fiscalYear is null)
        {
            return load;
        }

        load.Periods = await fiscalPeriodRepository.GetByFiscalYearAsync(fiscalYear.Id);
        load.CurrentFiscalMonth = CurrentFiscalMonth(fiscalYear, load.Periods, load.BusinessDate);

        // ---- requested period, needed for the filter options even when there is no scorecard
        load.RequestedPeriodType = ParsePeriodType(filter?.PeriodType).ToString();
        load.RequestedQuarter = Clamp(filter?.Quarter, 1, 4);
        load.RequestedFromMonth = Clamp(filter?.FromMonth, 1, 12);
        load.RequestedToMonth = Clamp(filter?.ToMonth, 1, 12);

        // ---- initiatives (no scorecard dependency: load even when the year has no scorecard,
        // ---- so initiative/budget widgets keep their data and their milestone delivery)
        if (load.Has(DashboardRoleCatalog.WidgetInitiativeStatus)
            || load.Has(DashboardRoleCatalog.WidgetInitiativeDeliveryChart)
            || load.Has(DashboardRoleCatalog.WidgetMilestoneTrend)
            || load.Has(DashboardRoleCatalog.WidgetBudgetSummary)
            || load.Has(DashboardRoleCatalog.WidgetBudgetAllocationChart)
            || load.Has(DashboardRoleCatalog.WidgetAlerts))
        {
            load.Initiatives = profile.DataScope == DashboardDataScope.AssignedOnly && currentUserService.UserId is Guid initiativeOwnerId
                ? await initiativeRepository.GetByOwnerIdWithMilestonesAsync(initiativeOwnerId)
                : await initiativeRepository.GetAllWithMilestonesAsync();
        }

        // ---- scorecards the caller may see, honouring the data scope
        var allScorecards = await scorecardRepository.GetAll();
        var inYear = allScorecards
            .Where(s => s.FiscalYearId == fiscalYear.Id)
            .OrderByDescending(s => s.ScorecardStatus == ScorecardStatus.Active)
            .ThenBy(s => s.Name)
            .ToList();

        if (profile.DataScope == DashboardDataScope.AssignedOnly)
        {
            // A data entry user may only work with scorecards that actually contain their KPIs.
            var userId = currentUserService.UserId;
            if (userId is Guid ownerId)
            {
                var ownedKpiScorecards = allScorecards
                    .Where(s => s.FiscalYearId == fiscalYear.Id)
                    .Select(s => s.Id)
                    .ToHashSet();

                var assignedScorecardIds = new HashSet<Guid>();
                foreach (var scorecardId in ownedKpiScorecards)
                {
                    var scorecardKpis = await kpiRepository.GetByScorecardAsync(scorecardId);
                    if (scorecardKpis.Any(k => k.OwnerId == ownerId || k.DivisionOwnerId == ownerId))
                    {
                        assignedScorecardIds.Add(scorecardId);
                    }
                }

                inYear = inYear.Where(s => assignedScorecardIds.Contains(s.Id)).ToList();
            }
            else
            {
                inYear = new List<Scorecard>();
            }
        }
        else if (!permissions.Contains(Permissions.ScorecardView))
        {
            // Without scorecard visibility the dashboard can still show what the role may read.
            inYear = inYear.Take(MaxScorecardsForComparison).ToList();
        }

        load.Scorecards = inYear;

        // A scorecard request is only honoured when the scorecard is inside the caller's
        // scope; anything else falls back to the default selection instead of failing.
        var requestedScorecard = filter?.ScorecardId is Guid requestedScorecardId
            ? inYear.FirstOrDefault(s => s.Id == requestedScorecardId)
            : null;

        load.SelectedScorecard = requestedScorecard
            ?? inYear.FirstOrDefault(s => s.ScorecardStatus == ScorecardStatus.Active)
            ?? inYear.FirstOrDefault();

        if (load.SelectedScorecard is null)
        {
            return load;
        }

        var selectedScorecard = load.SelectedScorecard;

        // ---- hierarchy for the selected scorecard
        var goals = await goalRepository.GetByScorecardAsync(selectedScorecard.Id);
        var kpis = await kpiRepository.GetByScorecardAsync(selectedScorecard.Id);

        if (profile.DataScope == DashboardDataScope.AssignedOnly)
        {
            kpis = kpis.Where(IsAssignedToCurrentUser).ToList();

            // Only keep goals that still own at least one assigned KPI, so nothing leaks.
            var referencedGoalIds = kpis
                .Where(k => k.Kra is not null)
                .Select(k => k.Kra!.GoalId)
                .ToHashSet();

            goals = goals.Where(g => referencedGoalIds.Contains(g.Id)).ToList();
        }

        load.Goals = goals;
        load.Kpis = kpis;
        load.KpiCountByGoal = goals.ToDictionary(
            g => g.Id,
            g => kpis.Count(k => k.Kra?.GoalId == g.Id));

        // ---- period scores from the shared calculation engine
        var periodType = Enum.Parse<PeriodType>(load.RequestedPeriodType);
        var quarter = load.RequestedQuarter;
        var fromMonth = load.RequestedFromMonth;
        var toMonth = load.RequestedToMonth;

        var comparisonScorecards = load.Scorecards.Take(MaxScorecardsForComparison).ToList();
        if (comparisonScorecards.All(s => s.Id != selectedScorecard.Id))
        {
            comparisonScorecards.Insert(0, selectedScorecard);
        }

        var periodResults = new Dictionary<Guid, ScorePeriodResult>();
        foreach (var scorecard in comparisonScorecards)
        {
            periodResults[scorecard.Id] = await scorePeriodCalculationService.CalculateAsync(
                scorecard.Id,
                fiscalYear.Id,
                periodType,
                quarter,
                fromMonth,
                toMonth,
                null,
                cancellationToken);
        }

        load.PeriodResults = periodResults;

        var selectedScorecardId = selectedScorecard.Id;
        if (periodResults.TryGetValue(selectedScorecardId, out var selectedResult))
        {
            load.SelectedPeriodResult = selectedResult;

            // Full year run feeds the trend widgets and the consecutive-red-month alert.
            if (periodType != PeriodType.Year)
            {
                load.SelectedYearResult = await scorePeriodCalculationService.CalculateAsync(
                    selectedScorecardId,
                    fiscalYear.Id,
                    PeriodType.Year,
                    null,
                    null,
                    null,
                    null,
                    cancellationToken);
            }
            else
            {
                load.SelectedYearResult = selectedResult;
            }
        }

        // ---- supporting data (scoped before projection)
        if (load.Kpis.Count > 0)
        {
            var kpiIds = load.Kpis.Select(k => k.Id).ToHashSet();
            load.Actuals = (await kpiActualValueRepository.GetForScorecardFiscalYearAsync(load.SelectedScorecard.Id, fiscalYear.Id))
                .Where(a => kpiIds.Contains(a.KpiId))
                .ToList();
            load.Targets = (await kpiTargetRepository.GetForScorecardFiscalYearAsync(load.SelectedScorecard.Id, fiscalYear.Id))
                .Where(t => kpiIds.Contains(t.KpiId))
                .ToList();
        }

        if (load.Has(DashboardRoleCatalog.WidgetPeriodLocks)
            || load.Has(DashboardRoleCatalog.WidgetDataEntryStatus)
            || load.Has(DashboardRoleCatalog.WidgetAlerts))
        {
            var configs = await periodLockConfigRepository.GetForScorecardFiscalYearAsync(load.SelectedScorecard.Id, fiscalYear.Id);
            var statuses = await periodLockStatusService.GetStatusesAsync(
                load.SelectedScorecard.Id,
                load.Periods,
                configs,
                load.BusinessDate);

            load.LocksByMonth = load.Periods
                .Select((period, index) => new { period.FiscalMonth, Info = statuses.ElementAtOrDefault(index) })
                .Where(x => x.Info is not null)
                .ToDictionary(x => x.FiscalMonth, x => x.Info!);
        }

        return load;
    }

    // ------------------------------------------------------------------ helpers

    private static PeriodType ParsePeriodType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "quarter" => PeriodType.Quarter,
        "month" => PeriodType.Month,
        "year" => PeriodType.Year,
        _ => PeriodType.Ytd
    };

    private static int? Clamp(int? value, int min, int max)
        => value is null ? null : Math.Clamp(value.Value, min, max);

    private static int CurrentFiscalMonth(FiscalYear fiscalYear, IReadOnlyList<FiscalPeriod> periods, DateTime businessDate)
    {
        if (periods.Count == 0)
        {
            return 0;
        }

        var period = periods.FirstOrDefault(p => businessDate >= p.StartDate && businessDate <= p.EndDate)
            ?? periods.Where(p => p.StartDate > businessDate).OrderBy(p => p.FiscalMonth).Select(p => (FiscalPeriod?)p).FirstOrDefault();

        return period?.FiscalMonth ?? 0;
    }

    private static string MonthLabel(FiscalPeriod period)
        => period.StartDate.ToString("MMM", CultureInfo.InvariantCulture);

    private static string MonthLabel(FiscalYear? fiscalYear, int fiscalMonth)
        => fiscalYear is null
            ? $"M{fiscalMonth}"
            : fiscalYear.StartDate.AddMonths(fiscalMonth - 1).ToString("MMM", CultureInfo.InvariantCulture);

    private static DashboardWidgetDto ToWidgetDto(DashboardWidgetDefinition definition) => new()
    {
        Key = definition.Key,
        Title = definition.Title,
        Description = definition.Description,
        Span = definition.Span
    };

    private static DashboardFilterDto BuildFilterDto(DashboardLoad load)
    {
        var fiscalYear = load.FiscalYear;
        var scope = load.SelectedPeriodResult?.Scope;

        return new DashboardFilterDto
        {
            FiscalYearId = fiscalYear?.Id,
            ScorecardId = load.SelectedScorecard?.Id,
            PeriodType = load.RequestedPeriodType,
            FiscalYearLabel = fiscalYear?.Label,
            ScorecardName = load.SelectedScorecard?.Name,
            PeriodLabel = PeriodLabel(scope, fiscalYear, load.SelectedScorecard?.Name),
            FiscalYearClosed = fiscalYear is not null && fiscalYear.FiscalYearStatus != FiscalYearStatus.Open,
            FromMonth = scope?.FromMonth,
            ToMonth = scope?.ToMonth,
            Quarter = scope is { Granularity: SnapshotGranularity.Quarter } s ? s.Quarter : null
        };
    }

    private static string PeriodLabel(ScorePeriodScope? scope, FiscalYear? fiscalYear, string? scorecardName)
    {
        if (scope is null)
        {
            return "YTD";
        }

        return scope.Granularity switch
        {
            SnapshotGranularity.Month when scope.FromMonth == scope.ToMonth =>
                MonthLabel(fiscalYear, scope.FromMonth),
            SnapshotGranularity.Month =>
                $"{MonthLabel(fiscalYear, scope.FromMonth)} - {MonthLabel(fiscalYear, scope.ToMonth)}",
            SnapshotGranularity.Quarter => $"Q{scope.Quarter}",
            SnapshotGranularity.Year => fiscalYear?.Label ?? "Full year",
            _ => "YTD"
        };
    }

    private async Task<IReadOnlyList<DashboardFilterOptionDto>> BuildFiscalYearOptionsAsync(
        DashboardLoad load, CancellationToken cancellationToken)
    {
        var years = await fiscalYearRepository.GetAll();
        return years
            .OrderByDescending(f => f.StartDate)
            .Select(f => new DashboardFilterOptionDto
            {
                Id = f.Id,
                Label = f.Label,
                IsSelected = load.FiscalYear?.Id == f.Id,
                Detail = f.FiscalYearStatus.ToString()
            })
            .ToList();
    }

    private IReadOnlyList<DashboardFilterOptionDto> BuildScorecardOptions(DashboardLoad load)
        => load.Scorecards
            .Select(s => new DashboardFilterOptionDto
            {
                Id = s.Id,
                Label = s.Name,
                IsSelected = load.SelectedScorecard?.Id == s.Id,
                Detail = s.Scope.ToString()
            })
            .ToList();

    private IReadOnlyList<DashboardPeriodOptionDto> BuildPeriodOptions(DashboardLoad load)
    {
        var fiscalYear = load.FiscalYear;
        var current = load.CurrentFiscalMonth <= 0 ? 12 : Math.Min(load.CurrentFiscalMonth, 12);

        // The engine scope is authoritative whenever a scorecard is in play. With no
        // scorecard in scope there is no snapshot yet, so fall back to the period that was
        // requested: the selector must still show what the dashboard is actually showing.
        var scope = load.SelectedPeriodResult?.Scope;
        var granularity = scope?.Granularity ?? ToGranularity(load.RequestedPeriodType);
        var appliedQuarter = scope?.Quarter ?? load.RequestedQuarter;
        var appliedFrom = scope?.FromMonth ?? load.RequestedFromMonth;
        var appliedTo = scope?.ToMonth ?? load.RequestedToMonth;

        var options = new List<DashboardPeriodOptionDto>
        {
            new() { Value = "Ytd", Label = "Year to date", IsSelected = granularity == SnapshotGranularity.Ytd }
        };

        for (var q = 1; q <= 4; q++)
        {
            var fromMonth = (q - 1) * 3 + 1;
            var available = fromMonth <= current;
            options.Add(new DashboardPeriodOptionDto
            {
                Value = $"Quarter:{q}",
                Label = $"Q{q} {MonthLabel(fiscalYear, fromMonth)} - {MonthLabel(fiscalYear, fromMonth + 2)}",
                IsSelected = granularity == SnapshotGranularity.Quarter && appliedQuarter == q,
                Detail = available ? null : "No data yet"
            });
        }

        for (var m = 1; m <= current; m++)
        {
            options.Add(new DashboardPeriodOptionDto
            {
                Value = $"Month:{m}",
                Label = MonthLabel(fiscalYear, m),
                IsSelected = granularity == SnapshotGranularity.Month && appliedFrom == m && appliedTo == m
            });
        }

        options.Add(new DashboardPeriodOptionDto
        {
            Value = "Year",
            Label = $"Full year {fiscalYear?.Label}".Trim(),
            IsSelected = granularity == SnapshotGranularity.Year
        });

        return options;
    }

    private static SnapshotGranularity ToGranularity(string? periodType) => periodType?.Trim().ToLowerInvariant() switch
    {
        "quarter" => SnapshotGranularity.Quarter,
        "month" => SnapshotGranularity.Month,
        "year" => SnapshotGranularity.Year,
        _ => SnapshotGranularity.Ytd
    };
}
