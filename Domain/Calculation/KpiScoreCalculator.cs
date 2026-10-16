using Domain.Calculation.ScoreStrategies;

namespace Domain.Calculation;

public interface IKpiScoreCalculator
{
    decimal? Calculate(KpiScoreRequest request);
}

public sealed class KpiScoreCalculator : IKpiScoreCalculator
{
    private readonly IScoreStrategyFactory strategyFactory;

    public KpiScoreCalculator(IScoreStrategyFactory strategyFactory)
    {
        this.strategyFactory = strategyFactory;
    }

    public decimal? Calculate(KpiScoreRequest request)
    {
        return strategyFactory.GetStrategy(request.OptimizationType).Score(request);
    }
}
