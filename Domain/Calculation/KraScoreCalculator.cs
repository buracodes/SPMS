using Domain.Enums;

namespace Domain.Calculation;

public sealed record KraScoreContribution(decimal? Score, decimal Weight, KpiType KpiType);

public interface IKraScoreCalculator
{
    decimal? Calculate(IReadOnlyList<KraScoreContribution> contributions);
}

public sealed class KraScoreCalculator : IKraScoreCalculator
{
    public decimal? Calculate(IReadOnlyList<KraScoreContribution> contributions)
    {
        var valid = contributions
            .Where(c => c.KpiType == KpiType.Lagging && c.Score.HasValue)
            .Select(c => (c.Score!.Value, c.Weight))
            .ToList();

        return WeightNormalizer.WeightedAverage(valid);
    }
}
