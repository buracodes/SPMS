namespace Domain.Calculation;

public sealed record ScorecardScoreContribution(decimal? Score, decimal Weight);

public interface IScorecardScoreCalculator
{
    decimal? Calculate(IReadOnlyList<ScorecardScoreContribution> contributions);
}

public sealed class ScorecardScoreCalculator : IScorecardScoreCalculator
{
    public decimal? Calculate(IReadOnlyList<ScorecardScoreContribution> contributions)
    {
        var valid = contributions.Where(c => c.Score.HasValue).ToList();
        if (valid.Count == 0)
        {
            return null;
        }

        if (valid.All(c => c.Weight <= 0m))
        {
            var average = valid.Average(c => c.Score!.Value);
            return average;
        }

        return WeightNormalizer.WeightedAverage(valid.Select(c => (c.Score!.Value, c.Weight)).ToList());
    }
}
