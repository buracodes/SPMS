using Application.Dashboard;
using Application.DTOS.Division;
using Application.DTOS.PerformanceMonitoring;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class PerformanceMonitoringService : IPerformanceMonitoringService
{
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKraRepository kraRepository;
    private readonly IUserRepository userRepository;
    private readonly IOrganizationalScopeService organizationalScope;
    private readonly IDivisionPeriodCalculationService divisionPeriodCalculationService;
    private readonly IScorePeriodCalculationService scorePeriodCalculationService;
    private readonly IRagThresholdService ragThresholdService;

    public PerformanceMonitoringService(
        IDivisionRepository divisionRepository,
        IDivisionPlanRepository divisionPlanRepository,
        IDivisionGoalRepository divisionGoalRepository,
        IScorecardRepository scorecardRepository,
        IFiscalYearRepository fiscalYearRepository,
        IFiscalPeriodRepository fiscalPeriodRepository,
        IKpiRepository kpiRepository,
        IGoalRepository goalRepository,
        IKraRepository kraRepository,
        IUserRepository userRepository,
        IOrganizationalScopeService organizationalScope,
        IDivisionPeriodCalculationService divisionPeriodCalculationService,
        IScorePeriodCalculationService scorePeriodCalculationService,
        IRagThresholdService ragThresholdService)
    {
        this.divisionRepository = divisionRepository;
        this.divisionPlanRepository = divisionPlanRepository;
        this.divisionGoalRepository = divisionGoalRepository;
        this.scorecardRepository = scorecardRepository;
        this.fiscalYearRepository = fiscalYearRepository;
        this.fiscalPeriodRepository = fiscalPeriodRepository;
        this.kpiRepository = kpiRepository;
        this.goalRepository = goalRepository;
        this.kraRepository = kraRepository;
        this.userRepository = userRepository;
        this.organizationalScope = organizationalScope;
        this.divisionPeriodCalculationService = divisionPeriodCalculationService;
        this.scorePeriodCalculationService = scorePeriodCalculationService;
        this.ragThresholdService = ragThresholdService;
    }

    /// <summary>
    /// RAG counts for a set of scores. A missing score is always NoData (never Red),
    /// matching DashboardRag / RagThresholds.Classify used everywhere else.
    /// </summary>
    private static (int Green, int Amber, int Red, int NoData) CountBands(
        IEnumerable<decimal?> scores,
        RagThresholds thresholds)
    {
        var green = 0;
        var amber = 0;
        var red = 0;
        var noData = 0;

        foreach (var score in scores)
        {
            switch (DashboardRag.FromScore(score, thresholds))
            {
                case DashboardRag.Green:
                    green++;
                    break;
                case DashboardRag.Amber:
                    amber++;
                    break;
                case DashboardRag.Red:
                    red++;
                    break;
                default:
                    noData++;
                    break;
            }
        }

        return (green, amber, red, noData);
    }

    public async Task<PerformanceMonitoringDto> GetAsync(PerformanceMonitoringFilter filter, CancellationToken cancellationToken = default)
    {
        var fiscalYear = filter.FiscalYearId.HasValue
            ? await fiscalYearRepository.GetById(filter.FiscalYearId.Value)
            : (await fiscalYearRepository.GetAll()).OrderByDescending(f => f.StartDate).FirstOrDefault();

        if (fiscalYear == null)
        {
            return new PerformanceMonitoringDto();
        }

        var scorecard = filter.ScorecardId.HasValue
            ? await scorecardRepository.GetById(filter.ScorecardId.Value)
            : (await scorecardRepository.GetAll()).FirstOrDefault(s => s.FiscalYearId == fiscalYear.Id && s.Scope == ScorecardScope.Corporate)
              ?? (await scorecardRepository.GetAll()).FirstOrDefault(s => s.FiscalYearId == fiscalYear.Id);

        if (scorecard == null)
        {
            return new PerformanceMonitoringDto();
        }

        if (scorecard.FiscalYearId != fiscalYear.Id)
        {
            throw new BadRequestException("The selected scorecard does not belong to the selected fiscal year.");
        }

        // Performance monitoring always works from the corporate scorecard; division
        // detail is reached by drilling into a division plan linked to it.
        if (scorecard.Scope != ScorecardScope.Corporate)
        {
            throw new BadRequestException("Select a corporate scorecard, then a division to inspect its performance.");
        }

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(fiscalYear.Id);
        var periodType = Enum.TryParse<PeriodType>(filter.PeriodType, true, out var pt) ? pt : (PeriodType?)null;

        // Resolve corporate visibility once from the live authorization state (ADR-011).
        var hasCorporateAccess = await organizationalScope.HasCorporateAccessAsync(cancellationToken);

        // One threshold load per request; every row/division reuses the same bands.
        var thresholds = await ragThresholdService.GetEffectiveAsync(cancellationToken: cancellationToken);

        var users = await userRepository.GetAllWithRolesAndDivisionAsync();
        var userNames = users.ToDictionary(u => u.Id, u => u.FullName);

        var activeDivisions = await divisionRepository.GetActiveAsync();
        // All divisions (active or not) resolve an owner's display name; only active ones
        // contribute comparison rows.
        var allDivisions = await divisionRepository.GetAll();
        var divisionNames = allDivisions.ToDictionary(d => d.Id, d => d.Name);

        if (organizationalScope.DivisionId.HasValue && !hasCorporateAccess)
        {
            return await GetDivisionAsync(
                fiscalYear,
                scorecard,
                periods,
                organizationalScope.DivisionId.Value,
                periodType,
                filter,
                thresholds,
                userNames,
                divisionNames,
                cancellationToken);
        }

        if (filter.DivisionId.HasValue)
        {
            await organizationalScope.EnsureDivisionAsync(filter.DivisionId.Value, cancellationToken);
            return await GetDivisionAsync(
                fiscalYear,
                scorecard,
                periods,
                filter.DivisionId.Value,
                periodType,
                filter,
                thresholds,
                userNames,
                divisionNames,
                cancellationToken);
        }

        if (!hasCorporateAccess)
        {
            throw new ForbiddenException("Performance monitoring requires corporate-wide visibility.");
        }

        return await GetCorporateAsync(
            fiscalYear,
            scorecard,
            periods,
            periodType,
            filter,
            thresholds,
            userNames,
            divisionNames,
            activeDivisions,
            cancellationToken);
    }

    private async Task<PerformanceMonitoringDto> GetCorporateAsync(
        FiscalYear fiscalYear,
        Scorecard scorecard,
        IReadOnlyList<FiscalPeriod> periods,
        PeriodType? periodType,
        PerformanceMonitoringFilter filter,
        RagThresholds thresholds,
        IReadOnlyDictionary<Guid, string> userNames,
        IReadOnlyDictionary<Guid, string> divisionNames,
        IReadOnlyList<Division> activeDivisions,
        CancellationToken cancellationToken)
    {
        var result = await scorePeriodCalculationService.CalculateAsync(
            scorecard.Id,
            fiscalYear.Id,
            periodType,
            filter.Quarter,
            filter.FromMonth,
            filter.ToMonth,
            filter.Year,
            cancellationToken);

        var kpis = kpiRepository.GetQueryable()
            .Where(k => k.ScorecardId == scorecard.Id && !k.IsDeleted && k.IsActive)
            .ToList();
        // GetQueryable() is no-tracking, so KPI navigation properties are null here. Load the
        // goal/KRA names through their repositories (Application has no EF Include).
        var scorecardGoals = await goalRepository.GetByScorecardAsync(scorecard.Id);
        var scorecardKras = kraRepository.GetQueryable()
            .Where(kr => kr.ScorecardId == scorecard.Id)
            .ToList();
        var goalById = scorecardGoals.ToDictionary(g => g.Id);
        var kraById = scorecardKras.ToDictionary(kr => kr.Id);
        var rows = new List<PerformanceKpiRowDto>();
        foreach (var k in kpis)
        {
            result.Kpis.TryGetValue(k.Id, out var kpiResult);
            string? goalName = null;
            string? kraName = null;
            if (k.KraId.HasValue && kraById.TryGetValue(k.KraId.Value, out var kr))
            {
                kraName = kr.Name;
                if (kr.GoalId != Guid.Empty && goalById.TryGetValue(kr.GoalId, out var g))
                {
                    goalName = g.Name;
                }
            }

            rows.Add(new PerformanceKpiRowDto
            {
                KpiId = k.Id,
                KpiName = k.Name,
                Unit = k.Unit,
                Weight = k.Weight,
                GoalName = goalName,
                KraName = kraName,
                OwnerName = k.OwnerId.HasValue && userNames.TryGetValue(k.OwnerId.Value, out var ownerName)
                    ? ownerName
                    : null,
                DivisionOwnerName = k.DivisionOwnerId.HasValue &&
                                    divisionNames.TryGetValue(k.DivisionOwnerId.Value, out var corporateDivisionOwner)
                    ? corporateDivisionOwner
                    : null,
                Target = kpiResult?.PeriodTarget,
                Actual = kpiResult?.PeriodActual,
                Score = kpiResult?.PeriodScore,
                Rag = DashboardRag.FromScore(kpiResult?.PeriodScore, thresholds)
            });
        }

        rows = rows.OrderByDescending(r => r.Weight).ToList();

        var divisionSummaries = new List<PerformanceDivisionSummaryDto>();
        foreach (var division in activeDivisions)
        {
            var plan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(division.Id, scorecard.Id);
            if (plan == null)
            {
                continue;
            }

            var divResult = await divisionPeriodCalculationService.CalculateAsync(
                plan.Id,
                scorecard.Id,
                fiscalYear.Id,
                periodType,
                filter.Quarter,
                filter.FromMonth,
                filter.ToMonth,
                filter.Year,
                cancellationToken);
            var goals = await divisionGoalRepository.GetByPlanWithKpisAsync(plan.Id);
            var counts = CountBands(divResult.Kpis.Values.Select(v => v.PeriodScore), thresholds);

            divisionSummaries.Add(new PerformanceDivisionSummaryDto
            {
                DivisionId = division.Id,
                DivisionName = division.Name,
                DivisionCode = division.Code,
                OverallScore = divResult.OverallScore,
                Rag = DashboardRag.FromScore(divResult.OverallScore, thresholds),
                KpiCount = divResult.Kpis.Count,
                GoalsCount = goals.Count,
                GreenCount = counts.Green,
                AmberCount = counts.Amber,
                RedCount = counts.Red,
                NoDataCount = counts.NoData,
                PlanStatus = plan.PlanStatus.ToString()
            });
        }

        var overallCounts = CountBands(rows.Select(r => r.Score), thresholds);

        return new PerformanceMonitoringDto
        {
            Scope = PerformanceScope.Corporate,
            FiscalYearId = fiscalYear.Id,
            FiscalYearName = fiscalYear.Label,
            ScorecardId = scorecard.Id,
            ScorecardName = scorecard.Name,
            OverallScore = result.Scorecards.Values.FirstOrDefault()?.PeriodScore,
            Rag = DashboardRag.FromScore(result.Scorecards.Values.FirstOrDefault()?.PeriodScore, thresholds),
            PeriodType = periodType?.ToString() ?? "Ytd",
            ScorecardCount = divisionSummaries.Count + 1,
            GoalsCount = scorecardGoals.Count,
            KpiCount = rows.Count,
            GreenCount = overallCounts.Green,
            AmberCount = overallCounts.Amber,
            RedCount = overallCounts.Red,
            NoDataCount = overallCounts.NoData,
            Kpis = rows,
            Divisions = divisionSummaries.OrderByDescending(d => d.OverallScore ?? 0).ToList(),
            FiscalPeriods = periods.OrderBy(p => p.FiscalMonth).Select(p => new DivisionFiscalPeriodDto
            {
                Id = p.Id,
                FiscalMonth = p.FiscalMonth,
                Quarter = p.Quarter,
                IsQuarterEnd = p.IsQuarterEnd,
                IsYearEnd = p.IsYearEnd
            }).ToList()
        };
    }

    private async Task<PerformanceMonitoringDto> GetDivisionAsync(
        FiscalYear fiscalYear,
        Scorecard scorecard,
        IReadOnlyList<FiscalPeriod> periods,
        Guid divisionId,
        PeriodType? periodType,
        PerformanceMonitoringFilter filter,
        RagThresholds thresholds,
        IReadOnlyDictionary<Guid, string> userNames,
        IReadOnlyDictionary<Guid, string> divisionNames,
        CancellationToken cancellationToken)
    {
        var division = await divisionRepository.GetById(divisionId);
        if (division == null)
        {
            throw new NotFoundException(nameof(Division), divisionId);
        }

        var emptyResult = new PerformanceMonitoringDto
        {
            Scope = PerformanceScope.Division,
            FiscalYearId = fiscalYear.Id,
            FiscalYearName = fiscalYear.Label,
            ScorecardId = scorecard.Id,
            ScorecardName = scorecard.Name,
            DivisionId = division.Id,
            DivisionName = division.Name,
            PeriodType = periodType?.ToString() ?? "Ytd",
            FiscalPeriods = periods.OrderBy(p => p.FiscalMonth).Select(p => new DivisionFiscalPeriodDto
            {
                Id = p.Id,
                FiscalMonth = p.FiscalMonth,
                Quarter = p.Quarter,
                IsQuarterEnd = p.IsQuarterEnd,
                IsYearEnd = p.IsYearEnd
            }).ToList()
        };

        var plan = await divisionPlanRepository.GetByDivisionAndScorecardWithDetailsAsync(divisionId, scorecard.Id);
        if (plan == null)
        {
            return emptyResult;
        }

        var result = await divisionPeriodCalculationService.CalculateAsync(
            plan.Id,
            scorecard.Id,
            fiscalYear.Id,
            periodType,
            filter.Quarter,
            filter.FromMonth,
            filter.ToMonth,
            filter.Year,
            cancellationToken);
        var goals = await divisionGoalRepository.GetByPlanWithKpisAsync(plan.Id);

        var goalNameById = goals.ToDictionary(g => g.Id, g => g.Name);
        var goalIds = goalNameById.Keys.ToList();

        var kpiEntities = kpiRepository.GetQueryable()
            .Where(k => k.DivisionGoalId != null && goalIds.Contains(k.DivisionGoalId.Value) && !k.IsDeleted && k.IsActive)
            .ToList()
            .ToDictionary(k => k.Id);

        var rows = new List<PerformanceKpiRowDto>();
        foreach (var kpi in kpiEntities.Values)
        {
            result.Kpis.TryGetValue(kpi.Id, out var kpiResult);

            string? goalName = null;
            if (kpi.DivisionGoalId != null && goalNameById.TryGetValue(kpi.DivisionGoalId.Value, out var gn))
            {
                goalName = gn;
            }

            rows.Add(new PerformanceKpiRowDto
            {
                KpiId = kpi.Id,
                KpiName = kpi.Name,
                Unit = kpi.Unit,
                Weight = kpi.Weight,
                GoalName = goalName,
                OwnerName = kpi.OwnerId.HasValue && userNames.TryGetValue(kpi.OwnerId.Value, out var ownerName)
                    ? ownerName
                    : null,
                DivisionOwnerName = kpi.DivisionOwnerId.HasValue &&
                                    divisionNames.TryGetValue(kpi.DivisionOwnerId.Value, out var divisionOwnerName)
                    ? divisionOwnerName
                    : null,
                Target = kpiResult?.PeriodTarget,
                Actual = kpiResult?.PeriodActual,
                Score = kpiResult?.PeriodScore,
                Rag = DashboardRag.FromScore(kpiResult?.PeriodScore, thresholds)
            });
        }

        rows = rows.OrderByDescending(r => r.Weight).ToList();
        var counts = CountBands(rows.Select(r => r.Score), thresholds);

        return new PerformanceMonitoringDto
        {
            Scope = PerformanceScope.Division,
            FiscalYearId = fiscalYear.Id,
            FiscalYearName = fiscalYear.Label,
            ScorecardId = scorecard.Id,
            ScorecardName = scorecard.Name,
            DivisionId = division.Id,
            DivisionName = division.Name,
            OverallScore = result.OverallScore,
            Rag = DashboardRag.FromScore(result.OverallScore, thresholds),
            PeriodType = periodType?.ToString() ?? "Ytd",
            ScorecardCount = 1,
            GoalsCount = goals.Count,
            KpiCount = rows.Count,
            GreenCount = counts.Green,
            AmberCount = counts.Amber,
            RedCount = counts.Red,
            NoDataCount = counts.NoData,
            Kpis = rows,
            FiscalPeriods = emptyResult.FiscalPeriods
        };
    }
}
