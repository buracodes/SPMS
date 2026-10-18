using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

/// <summary>
/// Computes period-scoped scores for a division plan: KPI → division goal → overall.
/// Reuses the domain calculators and the same period scope rules as the scorecard
/// engine so division KPIs honor YTD / Quarter / Month / Year filters identically.
/// </summary>
public sealed class DivisionPeriodCalculationService : IDivisionPeriodCalculationService
{
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IKpiScoreCalculator kpiScoreCalculator;
    private readonly IGoalScoreCalculator goalScoreCalculator;
    private readonly IScorecardScoreCalculator scorecardScoreCalculator;
    private readonly IBusinessClock businessClock;

    public DivisionPeriodCalculationService(
        IDivisionGoalRepository _divisionGoalRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IKpiScoreCalculator _kpiScoreCalculator,
        IGoalScoreCalculator _goalScoreCalculator,
        IScorecardScoreCalculator _scorecardScoreCalculator,
        IBusinessClock _businessClock)
    {
        divisionGoalRepository = _divisionGoalRepository;
        fiscalYearRepository = _fiscalYearRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        kpiTargetRepository = _kpiTargetRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        kpiScoreCalculator = _kpiScoreCalculator;
        goalScoreCalculator = _goalScoreCalculator;
        scorecardScoreCalculator = _scorecardScoreCalculator;
        businessClock = _businessClock;
    }

    public async Task<DivisionPeriodResult> CalculateAsync(
        Guid divisionPlanId,
        Guid scorecardId,
        Guid fiscalYearId,
        PeriodType? periodType,
        int? quarter,
        int? fromMonth,
        int? toMonth,
        int? year,
        CancellationToken cancellationToken = default)
    {
        var fiscalYear = await fiscalYearRepository.GetById(fiscalYearId);
        if (fiscalYear == null)
            return new DivisionPeriodResult();

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(fiscalYearId);
        var periodByMonth = periods.ToDictionary(p => p.FiscalMonth, p => p.Id);
        var currentFiscalMonth = ScorePeriodScopeResolver.GetCurrentFiscalMonth(fiscalYear, businessClock.BusinessDate);
        var scope = ScorePeriodScopeResolver.Resolve(periodType, quarter, fromMonth, toMonth, currentFiscalMonth);

        var goals = await divisionGoalRepository.GetByPlanWithKpisAsync(divisionPlanId);
        var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);
        var actuals = await kpiActualValueRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);

        var actualByKpiMonth = BuildLatestActualsByKpiMonth(actuals);
        var targetsByKpi = targets.GroupBy(t => t.KpiId).ToDictionary(g => g.Key, g => g.ToList());

        var kpiPeriods = new Dictionary<Guid, KpiScorePeriod>();
        var goalScores = new Dictionary<Guid, EntityScorePeriod>();

        foreach (var goal in goals)
        {
            var goalKpis = goal.Kpis.ToList();

            foreach (var kpi in goalKpis)
            {
                if (!kpiPeriods.ContainsKey(kpi.Id))
                {
                    kpiPeriods[kpi.Id] = ComputeKpiPeriod(kpi, scope, targetsByKpi, actualByKpiMonth);
                }
            }

            var periodContributions = goalKpis
                .Select(kpi =>
                {
                    kpiPeriods.TryGetValue(kpi.Id, out var per);
                    return new GoalScoreContribution(per?.PeriodScore, kpi.Weight);
                })
                .ToList();
            var periodScore = goalScoreCalculator.Calculate(periodContributions);

            var monthlyScores = new Dictionary<int, decimal?>();
            for (int m = scope.FromMonth; m <= scope.ToMonth; m++)
            {
                var monthlyContributions = goalKpis
                    .Select(kpi =>
                    {
                        kpiPeriods.TryGetValue(kpi.Id, out var per);
                        var ms = per != null && per.MonthlyScores.TryGetValue(m, out var val) ? val : (decimal?)null;
                        return new GoalScoreContribution(ms, kpi.Weight);
                    })
                    .ToList();
                monthlyScores[m] = goalScoreCalculator.Calculate(monthlyContributions);
            }

            goalScores[goal.Id] = new EntityScorePeriod
            {
                PeriodScore = periodScore,
                MonthlyScores = monthlyScores
            };
        }

        var overall = scorecardScoreCalculator.Calculate(
            goals.Select(g =>
            {
                goalScores.TryGetValue(g.Id, out var per);
                return new ScorecardScoreContribution(per?.PeriodScore, g.Weight);
            }).ToList());

        return new DivisionPeriodResult
        {
            Scope = scope,
            Kpis = kpiPeriods,
            DivisionGoals = goalScores,
            OverallScore = overall,
            FiscalPeriodByMonth = periodByMonth
        };
    }

    private static Dictionary<Guid, Dictionary<int, decimal>> BuildLatestActualsByKpiMonth(
        IReadOnlyList<KpiActualValue> actuals)
    {
        // Actual values are ordered latest-first by the repository; the first value
        // seen for a (kpi, month) is therefore the current one.
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
}
