using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public sealed class DivisionScoreCalculationService : IDivisionScoreCalculationService
{
    private readonly IKpiRepository kpiRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IKpiScoreSnapshotRepository kpiSnapshotRepository;
    private readonly IDivisionScoreSnapshotRepository divisionSnapshotRepository;
    private readonly IKpiScoreCalculator kpiScoreCalculator;
    private readonly IGoalScoreCalculator goalScoreCalculator;
    private readonly IScorecardRepository scorecardRepository;

    public DivisionScoreCalculationService(
        IKpiRepository _kpiRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IKpiScoreSnapshotRepository _kpiSnapshotRepository,
        IDivisionScoreSnapshotRepository _divisionSnapshotRepository,
        IKpiScoreCalculator _kpiScoreCalculator,
        IGoalScoreCalculator _goalScoreCalculator,
        IScorecardRepository _scorecardRepository)
    {
        kpiRepository = _kpiRepository;
        divisionGoalRepository = _divisionGoalRepository;
        divisionPlanRepository = _divisionPlanRepository;
        kpiTargetRepository = _kpiTargetRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        kpiSnapshotRepository = _kpiSnapshotRepository;
        divisionSnapshotRepository = _divisionSnapshotRepository;
        kpiScoreCalculator = _kpiScoreCalculator;
        goalScoreCalculator = _goalScoreCalculator;
        scorecardRepository = _scorecardRepository;
    }

    public async Task RecalculateDivisionGoalAsync(Guid divisionGoalId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        var divisionGoal = await divisionGoalRepository.GetByIdWithPlanAsync(divisionGoalId);
        if (divisionGoal == null || divisionGoal.DivisionPlan == null)
        {
            return;
        }

        var scorecard = await scorecardRepository.GetById(divisionGoal.DivisionPlan.ScorecardId);
        if (scorecard == null)
        {
            return;
        }

        var kpis = kpiRepository.GetQueryable()
            .Where(k => k.DivisionGoalId == divisionGoalId && !k.IsDeleted)
            .ToList();

        var contributions = new List<GoalScoreContribution>();
        foreach (var kpi in kpis)
        {
            var actual = await kpiActualValueRepository.GetLatestAsync(kpi.Id, fiscalPeriodId);
            
            var targets = await kpiTargetRepository.GetByKpiAndFiscalYearAsync(kpi.Id, scorecard.FiscalYearId);
            var target = targets.FirstOrDefault(t => t.Granularity == TargetGranularity.Monthly && t.FiscalPeriodId == fiscalPeriodId);

            decimal? score = null;
            if (actual != null && target != null)
            {
                score = kpiScoreCalculator.Calculate(new KpiScoreRequest(actual.Value, target.TargetValue, kpi.OptimizationType, kpi.ToleranceBandPercent));
            }

            await SaveKpiSnapshotAsync(kpi, scorecard.Id, fiscalPeriodId, score, actual?.Value, target?.TargetValue);

            contributions.Add(new GoalScoreContribution(score, kpi.Weight));
        }

        var goalScore = goalScoreCalculator.Calculate(contributions);
        await SaveDivisionGoalSnapshotAsync(divisionGoal, fiscalPeriodId, goalScore);
    }

    public async Task RecalculateDivisionCascadeAsync(Guid divisionGoalId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        await RecalculateDivisionGoalAsync(divisionGoalId, fiscalPeriodId, cancellationToken);
    }

    private async Task SaveKpiSnapshotAsync(Kpi kpi, Guid scorecardId, Guid fiscalPeriodId, decimal? score, decimal? actualValue, decimal? targetValue)
    {
        var latest = await kpiSnapshotRepository.GetLatestAsync(kpi.Id, fiscalPeriodId, SnapshotGranularity.Month);
        if (latest?.Score == score)
        {
            return;
        }

        await kpiSnapshotRepository.Add(new KpiScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            KpiId = kpi.Id,
            FiscalPeriodId = fiscalPeriodId,
            Granularity = SnapshotGranularity.Month,
            Score = score,
            ActualValue = actualValue,
            TargetValue = targetValue,
            CalculatedAt = DateTime.UtcNow
        });
    }

    private async Task SaveDivisionGoalSnapshotAsync(DivisionGoal divisionGoal, Guid fiscalPeriodId, decimal? score)
    {
        var latest = await divisionSnapshotRepository.GetLatestAsync(divisionGoal.Id, fiscalPeriodId, SnapshotGranularity.Month);
        if (latest?.Score == score)
        {
            return;
        }

        await divisionSnapshotRepository.Add(new DivisionScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = divisionGoal.DivisionPlan!.ScorecardId,
            DivisionId = divisionGoal.DivisionPlan.DivisionId,
            DivisionGoalId = divisionGoal.Id,
            FiscalPeriodId = fiscalPeriodId,
            Granularity = SnapshotGranularity.Month,
            Score = score,
            CalculatedAt = DateTime.UtcNow
        });
    }
}
