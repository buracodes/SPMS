using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public sealed class BinaryStrategy : IScoreStrategy
{
    public KpiOptimizationType OptimizationType => KpiOptimizationType.Binary;

    public decimal? Score(KpiScoreRequest request)
    {
        if (request.TargetValue == 0)
        {
            return null;
        }

        return request.ActualValue >= request.TargetValue ? 100m : 0m;
    }
}
