using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public interface IScoreStrategy
{
    KpiOptimizationType OptimizationType { get; }

    decimal? Score(KpiScoreRequest request);
}
