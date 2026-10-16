using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public sealed class MinimizeStrategy : IScoreStrategy
{
    public KpiOptimizationType OptimizationType => KpiOptimizationType.Minimize;

    public decimal? Score(KpiScoreRequest request)
    {
        if (request.ActualValue == 0)
        {
            return 0m;
        }

        if (request.TargetValue == 0)
        {
            return null;
        }

        return (request.TargetValue / request.ActualValue) * 100m;
    }
}
