using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public sealed class ScoreCalculationService : IScoreCalculationService
{
    private readonly IKpiRepository kpiRepository;
    private readonly IKraRepository kraRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IKpiScoreSnapshotRepository kpiSnapshotRepository;
    private readonly IKraScoreSnapshotRepository kraSnapshotRepository;
    private readonly IGoalScoreSnapshotRepository goalSnapshotRepository;
    private readonly IScorecardScoreSnapshotRepository scorecardSnapshotRepository;
    private readonly IKpiScoreCalculator kpiScoreCalculator;
    private readonly IKraScoreCalculator kraScoreCalculator;
    private readonly IGoalScoreCalculator goalScoreCalculator;
    private readonly IScorecardScoreCalculator scorecardScoreCalculator;

    public ScoreCalculationService(
        IKpiRepository _kpiRepository,
        IKraRepository _kraRepository,
        IGoalRepository _goalRepository,
        IScorecardRepository _scorecardRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IKpiScoreSnapshotRepository _kpiSnapshotRepository,
        IKraScoreSnapshotRepository _kraSnapshotRepository,
        IGoalScoreSnapshotRepository _goalSnapshotRepository,
        IScorecardScoreSnapshotRepository _scorecardSnapshotRepository,
        IKpiScoreCalculator _kpiScoreCalculator,
        IKraScoreCalculator _kraScoreCalculator,
        IGoalScoreCalculator _goalScoreCalculator,
        IScorecardScoreCalculator _scorecardScoreCalculator)
    {
        kpiRepository = _kpiRepository;
        kraRepository = _kraRepository;
        goalRepository = _goalRepository;
        scorecardRepository = _scorecardRepository;
        kpiTargetRepository = _kpiTargetRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        kpiSnapshotRepository = _kpiSnapshotRepository;
        kraSnapshotRepository = _kraSnapshotRepository;
        goalSnapshotRepository = _goalSnapshotRepository;
        scorecardSnapshotRepository = _scorecardSnapshotRepository;
        kpiScoreCalculator = _kpiScoreCalculator;
        kraScoreCalculator = _kraScoreCalculator;
        goalScoreCalculator = _goalScoreCalculator;
        scorecardScoreCalculator = _scorecardScoreCalculator;
    }

    public async Task RecalculateKpiAsync(Guid kpiId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        var kpi = await kpiRepository.GetByIdWithScorecardAsync(kpiId);
        if (kpi == null || kpi.Scorecard == null)
        {
            return;
        }

        await RecalculateSingleKpiAsync(kpi, kpi.Scorecard.FiscalYearId, fiscalPeriodId);
    }

    public async Task RecalculateKpiCascadeAsync(Guid kpiId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        var kpi = await kpiRepository.GetByIdWithScorecardAsync(kpiId);
        if (kpi == null || kpi.Scorecard == null)
        {
            return;
        }

        await RecalculateKpiAsync(kpiId, fiscalPeriodId, cancellationToken);

        if (kpi.KraId.HasValue)
        {
            await RecalculateUpwardCascadeAsync(kpi.KraId.Value, fiscalPeriodId, cancellationToken);
        }
    }

    public async Task RecalculateUpwardCascadeAsync(Guid kraId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        var kra = await kraRepository.GetByIdWithKpisAsync(kraId);
        if (kra == null)
        {
            return;
        }

        var kraScore = await ComputeKraScoreAsync(kra, fiscalPeriodId);
        await SaveKraSnapshotAsync(kra, fiscalPeriodId, kraScore);

        var goal = await goalRepository.GetByIdWithKrasAsync(kra.GoalId);
        if (goal == null)
        {
            return;
        }

        var goalScore = await ComputeGoalScoreAsync(goal, fiscalPeriodId);
        await SaveGoalSnapshotAsync(goal, fiscalPeriodId, goalScore);

        var scorecard = await scorecardRepository.GetByIdWithGoalsAsync(goal.ScorecardId);
        if (scorecard == null)
        {
            return;
        }

        var scorecardScore = await ComputeScorecardScoreAsync(scorecard, fiscalPeriodId);
        await SaveScorecardSnapshotAsync(scorecard, fiscalPeriodId, scorecardScore);
    }

    public async Task RecalculateScorecardAsync(Guid scorecardId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        var scorecard = await scorecardRepository.GetByIdWithGoalsAsync(scorecardId);
        if (scorecard == null)
        {
            return;
        }

        foreach (var goal in scorecard.Goals)
        {
            var goalWithKras = await goalRepository.GetByIdWithKrasAsync(goal.Id);
            if (goalWithKras == null)
            {
                continue;
            }

            foreach (var kra in goalWithKras.Kras)
            {
                foreach (var kpi in kra.Kpis)
                {
                    await RecalculateSingleKpiAsync(kpi, scorecard.FiscalYearId, fiscalPeriodId);
                }

                var kraScore = await ComputeKraScoreAsync(kra, fiscalPeriodId);
                await SaveKraSnapshotAsync(kra, fiscalPeriodId, kraScore);
            }

            var goalScore = await ComputeGoalScoreAsync(goalWithKras, fiscalPeriodId);
            await SaveGoalSnapshotAsync(goalWithKras, fiscalPeriodId, goalScore);
        }

        var scorecardScore = await ComputeScorecardScoreAsync(scorecard, fiscalPeriodId);
        await SaveScorecardSnapshotAsync(scorecard, fiscalPeriodId, scorecardScore);
    }

    private async Task RecalculateSingleKpiAsync(Kpi kpi, Guid fiscalYearId, Guid fiscalPeriodId)
    {
        var actual = await kpiActualValueRepository.GetLatestAsync(kpi.Id, fiscalPeriodId);
        var target = (await kpiTargetRepository.GetByKpiAndFiscalYearAsync(kpi.Id, fiscalYearId))
            .FirstOrDefault(t => t.Granularity == TargetGranularity.Monthly && t.FiscalPeriodId == fiscalPeriodId);

        decimal? score = null;
        if (actual != null && target != null)
        {
            score = kpiScoreCalculator.Calculate(new KpiScoreRequest(actual.Value, target.TargetValue, kpi.OptimizationType, kpi.ToleranceBandPercent));
        }

        await SaveKpiSnapshotAsync(kpi, fiscalPeriodId, score, actual?.Value, target?.TargetValue);
    }

    private async Task<decimal?> ComputeKraScoreAsync(Kra kra, Guid fiscalPeriodId)
    {
        var contributions = new List<KraScoreContribution>();
        foreach (var kpi in kra.Kpis)
        {
            var snapshot = await kpiSnapshotRepository.GetLatestAsync(kpi.Id, fiscalPeriodId, SnapshotGranularity.Month);
            contributions.Add(new KraScoreContribution(snapshot?.Score, kpi.Weight, kpi.KpiType));
        }

        return kraScoreCalculator.Calculate(contributions);
    }

    private async Task<decimal?> ComputeGoalScoreAsync(Goal goal, Guid fiscalPeriodId)
    {
        var contributions = new List<GoalScoreContribution>();
        foreach (var kra in goal.Kras)
        {
            var snapshot = await kraSnapshotRepository.GetLatestAsync(kra.Id, fiscalPeriodId, SnapshotGranularity.Month);
            contributions.Add(new GoalScoreContribution(snapshot?.Score, kra.Weight));
        }

        return goalScoreCalculator.Calculate(contributions);
    }

    private async Task<decimal?> ComputeScorecardScoreAsync(Scorecard scorecard, Guid fiscalPeriodId)
    {
        var contributions = new List<ScorecardScoreContribution>();
        foreach (var goal in scorecard.Goals)
        {
            // Inactive goals are business-deactivated (toggle in FR-302 edit) and are
            // excluded from reporting the same way, so they contribute neither score
            // nor normalization weight to the scorecard aggregate. Missing data stays
            // out via snapshot?.Score == null per FR-604; never coerced to zero.
            if (!goal.IsActive)
            {
                continue;
            }

            var snapshot = await goalSnapshotRepository.GetLatestAsync(goal.Id, fiscalPeriodId, SnapshotGranularity.Month);
            contributions.Add(new ScorecardScoreContribution(snapshot?.Score, goal.Weight));
        }

        return scorecardScoreCalculator.Calculate(contributions);
    }

    private async Task SaveKpiSnapshotAsync(Kpi kpi, Guid fiscalPeriodId, decimal? score, decimal? actualValue, decimal? targetValue)
    {
        var latest = await kpiSnapshotRepository.GetLatestAsync(kpi.Id, fiscalPeriodId, SnapshotGranularity.Month);
        if (latest?.Score == score)
        {
            return;
        }

        await kpiSnapshotRepository.Add(new KpiScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = kpi.ScorecardId,
            KpiId = kpi.Id,
            FiscalPeriodId = fiscalPeriodId,
            Granularity = SnapshotGranularity.Month,
            Score = score,
            ActualValue = actualValue,
            TargetValue = targetValue,
            CalculatedAt = DateTime.UtcNow
        });
    }

    private async Task SaveKraSnapshotAsync(Kra kra, Guid fiscalPeriodId, decimal? score)
    {
        var latest = await kraSnapshotRepository.GetLatestAsync(kra.Id, fiscalPeriodId, SnapshotGranularity.Month);
        if (latest?.Score == score)
        {
            return;
        }

        await kraSnapshotRepository.Add(new KraScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = kra.ScorecardId,
            KraId = kra.Id,
            FiscalPeriodId = fiscalPeriodId,
            Granularity = SnapshotGranularity.Month,
            Score = score,
            CalculatedAt = DateTime.UtcNow
        });
    }

    private async Task SaveGoalSnapshotAsync(Goal goal, Guid fiscalPeriodId, decimal? score)
    {
        var latest = await goalSnapshotRepository.GetLatestAsync(goal.Id, fiscalPeriodId, SnapshotGranularity.Month);
        if (latest?.Score == score)
        {
            return;
        }

        await goalSnapshotRepository.Add(new GoalScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = goal.ScorecardId,
            GoalId = goal.Id,
            FiscalPeriodId = fiscalPeriodId,
            Granularity = SnapshotGranularity.Month,
            Score = score,
            CalculatedAt = DateTime.UtcNow
        });
    }

    private async Task SaveScorecardSnapshotAsync(Scorecard scorecard, Guid fiscalPeriodId, decimal? score)
    {
        var latest = await scorecardSnapshotRepository.GetLatestAsync(scorecard.Id, fiscalPeriodId, SnapshotGranularity.Month);
        if (latest?.Score == score)
        {
            return;
        }

        await scorecardSnapshotRepository.Add(new ScorecardScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            FiscalPeriodId = fiscalPeriodId,
            Granularity = SnapshotGranularity.Month,
            Score = score,
            CalculatedAt = DateTime.UtcNow
        });
    }
}
