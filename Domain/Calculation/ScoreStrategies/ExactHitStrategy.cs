using Domain.Enums;

namespace Domain.Calculation.ScoreStrategies;

public sealed class ExactHitStrategy : IScoreStrategy
{
    public KpiOptimizationType OptimizationType => KpiOptimizationType.ExactHit;

    public decimal? Score(KpiScoreRequest request)
    {
        if (request.TargetValue == 0)
        {
            return null;
        }

        if (request.ToleranceBandPercent.HasValue)
        {
            var deviation = System.Math.Abs(request.ActualValue - request.TargetValue) / request.TargetValue * 100m;
            return deviation <= request.ToleranceBandPercent.Value ? 100m : 0m;
        }

        return request.ActualValue == request.TargetValue ? 100m : 0m;
    }
}
