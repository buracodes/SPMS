using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public sealed class ScorePeriodCalculationService : IScorePeriodCalculationService
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IKpiScoreCalculator kpiScoreCalculator;
    private readonly IKraScoreCalculator kraScoreCalculator;
    private readonly IGoalScoreCalculator goalScoreCalculator;
    private readonly IScorecardScoreCalculator scorecardScoreCalculator;
    private readonly IBusinessClock businessClock;

    public ScorePeriodCalculationService(
        IScorecardRepository _scorecardRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IGoalRepository _goalRepository,
        IKpiRepository _kpiRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IKpiScoreCalculator _kpiScoreCalculator,
        IKraScoreCalculator _kraScoreCalculator,
        IGoalScoreCalculator _goalScoreCalculator,
        IScorecardScoreCalculator _scorecardScoreCalculator,
        IBusinessClock _businessClock)
    {
        scorecardRepository = _scorecardRepository;
        fiscalYearRepository = _fiscalYearRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        goalRepository = _goalRepository;
        kpiRepository = _kpiRepository;
        kpiTargetRepository = _kpiTargetRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        kpiScoreCalculator = _kpiScoreCalculator;
        kraScoreCalculator = _kraScoreCalculator;
        goalScoreCalculator = _goalScoreCalculator;
        scorecardScoreCalculator = _scorecardScoreCalculator;
        businessClock = _businessClock;
    }

    public async Task<ScorePeriodResult> CalculateAsync(
        Guid scorecardId,
        Guid fiscalYearId,
        PeriodType? periodType,
        int? quarter,
        int? fromMonth,
        int? toMonth,
        int? year,
        CancellationToken cancellationToken = default)
    {
        var scorecard = await scorecardRepository.GetById(scorecardId);
        if (scorecard == null || scorecard.FiscalYearId != fiscalYearId)
            return new ScorePeriodResult();

        var fiscalYear = await fiscalYearRepository.GetById(fiscalYearId);
        if (fiscalYear == null)
            return new ScorePeriodResult();

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(fiscalYearId);
        var periodByMonth = periods.ToDictionary(p => p.FiscalMonth, p => p.Id);
        var currentFiscalMonth = ScorePeriodScopeResolver.GetCurrentFiscalMonth(fiscalYear, businessClock.BusinessDate);
        var scope = ScorePeriodScopeResolver.Resolve(periodType, quarter, fromMonth, toMonth, currentFiscalMonth);

        var goals = await goalRepository.GetByScorecardAsync(scorecardId);
        var kpis = await kpiRepository.GetByScorecardAsync(scorecardId);
        var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);
        var actuals = await kpiActualValueRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);

        var actualByKpiMonth = BuildLatestActualsByKpiMonth(actuals);
        var targetsByKpi = targets.GroupBy(t => t.KpiId).ToDictionary(g => g.Key, g => g.ToList());

        var kpiPeriods = new Dictionary<Guid, KpiScorePeriod>();
        foreach (var kpi in kpis)
        {
            kpiPeriods[kpi.Id] = ComputeKpiPeriod(kpi, scope, targetsByKpi, actualByKpiMonth);
        }

        // Build Kra → GoalId + Weight mapping from KPI nav properties
        var kraInfo = new Dictionary<Guid, (Guid GoalId, decimal Weight)>();
        foreach (var kpi in kpis)
        {
            if (kpi.KraId.HasValue && kpi.Kra != null && !kraInfo.ContainsKey(kpi.KraId.Value))
            {
                kraInfo[kpi.KraId.Value] = (kpi.Kra.GoalId, kpi.Kra.Weight);
            }
        }

        var kraScores = ComputeKraScores(kpis, kpiPeriods, scope);
        var goalScores = ComputeGoalScores(goals, kraScores, kraInfo, scope);
        var scorecardScores = ComputeScorecardScores(scorecardId, goals, goalScores, scope, scorecardScoreCalculator);

        return new ScorePeriodResult
        {
            Scope = scope,
            Kpis = kpiPeriods,
            Kras = kraScores,
            Goals = goalScores,
            Scorecards = scorecardScores,
            FiscalPeriodByMonth = periodByMonth
        };
    }

    private static Dictionary<Guid, Dictionary<int, decimal>> BuildLatestActualsByKpiMonth(
        IReadOnlyList<KpiActualValue> actuals)
    {
        var result = new Dictionary<Guid, Dictionary<int, decimal>>();
        foreach (var a in actuals)
        {
            if (a.FiscalPeriod == null) continue;
            var month = a.FiscalPeriod.FiscalMonth;
            if (month is < 1 or > 12) continue;
            if (!result.TryGetValue(a.KpiId, out var byMonth))
            {
                byMonth = new Dictionary<int, decimal>();
                result[a.KpiId] = byMonth;
            }
            if (!byMonth.ContainsKey(month))
                byMonth[month] = a.Value;
        }
        return result;
    }

    private KpiScorePeriod ComputeKpiPeriod(
        Kpi kpi,
        ScorePeriodScope scope,
        Dictionary<Guid, List<KpiTarget>> targetsByKpi,
        Dictionary<Guid, Dictionary<int, decimal>> actualByKpiMonth)
    {
        var kpiTargets = targetsByKpi.TryGetValue(kpi.Id, out var list) ? list : new List<KpiTarget>();
        var monthlyTargetByMonth = new Dictionary<int, decimal>();
        var quarterlyTargetByQuarter = new Dictionary<int, decimal>();
        decimal? annualTarget = null;

        foreach (var t in kpiTargets)
        {
            if (t.FiscalPeriod == null) continue;
            switch (t.Granularity)
            {
                case TargetGranularity.Monthly:
                    monthlyTargetByMonth[t.FiscalPeriod.FiscalMonth] = t.TargetValue;
                    break;
                case TargetGranularity.Quarterly:
                    quarterlyTargetByQuarter[t.FiscalPeriod.Quarter] = t.TargetValue;
                    break;
                case TargetGranularity.Annual:
                    annualTarget = t.TargetValue;
                    break;
            }
        }

        var actualsByMonth = actualByKpiMonth.TryGetValue(kpi.Id, out var am)
            ? am : new Dictionary<int, decimal>();

        var monthlyScores = new Dictionary<int, decimal?>();
        var monthlyTargets = new Dictionary<int, decimal?>();
        var monthlyActuals = new Dictionary<int, decimal?>();

        for (int m = scope.FromMonth; m <= scope.ToMonth; m++)
        {
            decimal? mt = monthlyTargetByMonth.TryGetValue(m, out var tv) ? tv : (decimal?)null;
            decimal? ma = actualsByMonth.TryGetValue(m, out var av) ? av : (decimal?)null;

            decimal? score = null;
            if (mt.HasValue && mt.Value != 0m && ma.HasValue)
                score = kpiScoreCalculator.Calculate(
                    new KpiScoreRequest(ma.Value, mt.Value, kpi.OptimizationType, kpi.ToleranceBandPercent));

            monthlyScores[m] = score;
            monthlyTargets[m] = mt;
            monthlyActuals[m] = ma;
        }

        decimal? periodActual = TimeAggregator.Aggregate(actualsByMonth, scope.FromMonth, scope.ToMonth, kpi.IsCumulative);
        decimal? periodTarget = scope.Granularity switch
        {
            SnapshotGranularity.Quarter when quarterlyTargetByQuarter.ContainsKey(scope.Quarter) =>
                quarterlyTargetByQuarter[scope.Quarter],
            SnapshotGranularity.Quarter =>
                TimeAggregator.Aggregate(monthlyTargetByMonth, scope.FromMonth, scope.ToMonth, kpi.IsCumulative),
            SnapshotGranularity.Year =>
                annualTarget ?? TimeAggregator.Aggregate(monthlyTargetByMonth, 1, 12, kpi.IsCumulative),
            _ =>
                TimeAggregator.Aggregate(monthlyTargetByMonth, scope.FromMonth, scope.ToMonth, kpi.IsCumulative)
        };

        decimal? periodScore = null;
        if (periodTarget.HasValue && periodTarget.Value > 0m && periodActual.HasValue)
            periodScore = kpiScoreCalculator.Calculate(
                new KpiScoreRequest(periodActual.Value, periodTarget.Value, kpi.OptimizationType, kpi.ToleranceBandPercent));

        return new KpiScorePeriod
        {
            PeriodScore = periodScore,
            PeriodTarget = periodTarget,
            PeriodActual = periodActual,
            MonthlyScores = monthlyScores,
            MonthlyTargets = monthlyTargets,
            MonthlyActuals = monthlyActuals
        };
    }

    private Dictionary<Guid, EntityScorePeriod> ComputeKraScores(
        IReadOnlyList<Kpi> kpis,
        Dictionary<Guid, KpiScorePeriod> kpiPeriods,
        ScorePeriodScope scope)
    {
        var kraKpis = kpis.Where(k => k.KraId.HasValue).GroupBy(k => k.KraId!.Value);
        var result = new Dictionary<Guid, EntityScorePeriod>();

        foreach (var group in kraKpis)
        {
            var kpisInKra = group.ToList();

            var periodContributions = kpisInKra.Select(kpi =>
            {
                kpiPeriods.TryGetValue(kpi.Id, out var per);
                return new KraScoreContribution(per?.PeriodScore, kpi.Weight, kpi.KpiType);
            }).ToList();

            var periodScore = kraScoreCalculator.Calculate(periodContributions);

            var monthlyScores = new Dictionary<int, decimal?>();
            for (int m = scope.FromMonth; m <= scope.ToMonth; m++)
            {
                var monthlyContributions = kpisInKra.Select(kpi =>
                {
                    kpiPeriods.TryGetValue(kpi.Id, out var per);
                    var ms = per != null && per.MonthlyScores.TryGetValue(m, out var val) ? val : (decimal?)null;
                    return new KraScoreContribution(ms, kpi.Weight, kpi.KpiType);
                }).ToList();
                monthlyScores[m] = kraScoreCalculator.Calculate(monthlyContributions);
            }

            result[group.Key] = new EntityScorePeriod { PeriodScore = periodScore, MonthlyScores = monthlyScores };
        }

        return result;
    }

    private Dictionary<Guid, EntityScorePeriod> ComputeGoalScores(
        IReadOnlyList<Goal> goals,
        IReadOnlyDictionary<Guid, EntityScorePeriod> kraScores,
        IReadOnlyDictionary<Guid, (Guid GoalId, decimal Weight)> kraInfo,
        ScorePeriodScope scope)
    {
        var krasByGoal = new Dictionary<Guid, List<(Guid KraId, decimal Weight)>>();
        foreach (var kv in kraInfo)
        {
            if (!krasByGoal.TryGetValue(kv.Value.GoalId, out var list))
            {
                list = new List<(Guid KraId, decimal Weight)>();
                krasByGoal[kv.Value.GoalId] = list;
            }
            list.Add((kv.Key, kv.Value.Weight));
        }

        var result = new Dictionary<Guid, EntityScorePeriod>();

        foreach (var goal in goals)
        {
            var kraList = krasByGoal.TryGetValue(goal.Id, out var kl) ? kl : new List<(Guid KraId, decimal Weight)>();

            var periodContributions = kraList.Select(kra =>
            {
                kraScores.TryGetValue(kra.KraId, out var per);
                return new GoalScoreContribution(per?.PeriodScore, kra.Weight);
            }).ToList();
            var periodScore = goalScoreCalculator.Calculate(periodContributions);

            var monthlyScores = new Dictionary<int, decimal?>();
            for (int m = scope.FromMonth; m <= scope.ToMonth; m++)
            {
                var monthlyContributions = kraList.Select(kra =>
                {
                    kraScores.TryGetValue(kra.KraId, out var per);
                    var ms = per != null && per.MonthlyScores.TryGetValue(m, out var val) ? val : (decimal?)null;
                    return new GoalScoreContribution(ms, kra.Weight);
                }).ToList();
                monthlyScores[m] = goalScoreCalculator.Calculate(monthlyContributions);
            }

            result[goal.Id] = new EntityScorePeriod { PeriodScore = periodScore, MonthlyScores = monthlyScores };
        }

        return result;
    }

    private static Dictionary<Guid, EntityScorePeriod> ComputeScorecardScores(
        Guid scorecardId,
        IReadOnlyList<Goal> goals,
        IReadOnlyDictionary<Guid, EntityScorePeriod> goalScores,
        ScorePeriodScope scope,
        IScorecardScoreCalculator scorecardCalculator)
    {
        // FR-604 roll-up: inactive goals are business-deactivated and excluded from the
        // scorecard aggregate (score and normalization denominator) exactly as reporting
        // does; their own period scores above remain viewable. Ensures formula-quality
        // consistency with ScoreCalculationService (snapshot engine).
        var scoringGoals = goals.Where(g => g.IsActive).ToList();

        var periodContributions = scoringGoals.Select(g =>
        {
            goalScores.TryGetValue(g.Id, out var per);
            return new ScorecardScoreContribution(per?.PeriodScore, g.Weight);
        }).ToList();

        var periodScore = scorecardCalculator.Calculate(periodContributions);

        var monthlyScores = new Dictionary<int, decimal?>();
        for (int m = scope.FromMonth; m <= scope.ToMonth; m++)
        {
            var monthlyContributions = scoringGoals.Select(g =>
            {
                goalScores.TryGetValue(g.Id, out var per);
                var ms = per != null && per.MonthlyScores.TryGetValue(m, out var val) ? val : (decimal?)null;
                return new ScorecardScoreContribution(ms, g.Weight);
            }).ToList();
            monthlyScores[m] = scorecardCalculator.Calculate(monthlyContributions);
        }

        return new Dictionary<Guid, EntityScorePeriod>
        {
            [scorecardId] = new EntityScorePeriod { PeriodScore = periodScore, MonthlyScores = monthlyScores }
        };
    }
}
