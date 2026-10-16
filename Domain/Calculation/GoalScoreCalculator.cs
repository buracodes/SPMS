namespace Domain.Calculation;

public sealed record GoalScoreContribution(decimal? Score, decimal Weight);

public interface IGoalScoreCalculator
{
    decimal? Calculate(IReadOnlyList<GoalScoreContribution> contributions);
}

public sealed class GoalScoreCalculator : IGoalScoreCalculator
{
    public decimal? Calculate(IReadOnlyList<GoalScoreContribution> contributions)
    {
        var valid = contributions
            .Where(c => c.Score.HasValue)
            .Select(c => (c.Score!.Value, c.Weight))
            .ToList();

        return WeightNormalizer.WeightedAverage(valid);
    }
}
