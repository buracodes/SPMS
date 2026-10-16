using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public sealed class ScoreStrategyFactory : IScoreStrategyFactory
{
    private readonly IReadOnlyDictionary<KpiOptimizationType, IScoreStrategy> strategies;

    public ScoreStrategyFactory()
    {
        strategies = new List<IScoreStrategy>
        {
            new MaximizeStrategy(),
            new MinimizeStrategy(),
            new ExactHitStrategy(),
            new BinaryStrategy()
        }.ToDictionary(s => s.OptimizationType);
    }

    public IScoreStrategy GetStrategy(KpiOptimizationType optimizationType)
    {
        return strategies.TryGetValue(optimizationType, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(nameof(optimizationType), optimizationType, $"No score strategy registered for optimization type '{optimizationType}'.");
    }
}
