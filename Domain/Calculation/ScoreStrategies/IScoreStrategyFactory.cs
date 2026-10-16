using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public interface IScoreStrategyFactory
{
    IScoreStrategy GetStrategy(KpiOptimizationType optimizationType);
}
