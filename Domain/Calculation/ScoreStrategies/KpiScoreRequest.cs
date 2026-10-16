using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public sealed record KpiScoreRequest(
    decimal ActualValue,
    decimal TargetValue,
    KpiOptimizationType OptimizationType,
    decimal? ToleranceBandPercent = null);
