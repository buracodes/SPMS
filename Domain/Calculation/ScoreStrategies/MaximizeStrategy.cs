using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public sealed class MaximizeStrategy : IScoreStrategy
{
    public KpiOptimizationType OptimizationType => KpiOptimizationType.Maximize;

    public decimal? Score(KpiScoreRequest request)
    {
        if (request.TargetValue == 0)
        {
            return null;
        }

        return (request.ActualValue / request.TargetValue) * 100m;
    }
}
