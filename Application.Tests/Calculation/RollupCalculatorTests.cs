using Domain.Calculation;

namespace Application.Tests.Calculation;

public class RollupCalculatorTests
{
    [Fact]
    public void GoalScore_WeightedAverageOfValidKras()
    {
        var calculator = new GoalScoreCalculator();

        var contributions = new List<GoalScoreContribution>
        {
            new(80m, 60m),
            new(60m, 40m)
        };

        var score = calculator.Calculate(contributions);

        Assert.Equal(72m, score);
    }

    [Fact]
    public void GoalScore_NullScoreContribution_Excluded()
    {
        var calculator = new GoalScoreCalculator();

        var contributions = new List<GoalScoreContribution>
        {
            new(80m, 60m),
            new(null, 40m)
        };

        var score = calculator.Calculate(contributions);

        Assert.Equal(80m, score);
    }

    [Fact]
    public void GoalScore_NoValidContributors_ReturnsNull()
    {
        var calculator = new GoalScoreCalculator();

        Assert.Null(calculator.Calculate(new List<GoalScoreContribution>()));
    }

    [Fact]
    public void ScorecardScore_WeightedAverageOfValidGoals()
    {
        var calculator = new ScorecardScoreCalculator();

        var contributions = new List<ScorecardScoreContribution>
        {
            new(90m, 70m),
            new(50m, 30m)
        };

        var score = calculator.Calculate(contributions);

        Assert.Equal(78m, score);
    }

    [Fact]
    public void ScorecardScore_AllWeightsZero_UsesEqualWeights()
    {
        var calculator = new ScorecardScoreCalculator();

        var contributions = new List<ScorecardScoreContribution>
        {
            new(100m, 0m),
            new(80m, 0m),
            new(60m, 0m)
        };

        var score = calculator.Calculate(contributions);

        Assert.Equal(80m, score);
    }

    [Fact]
    public void ScorecardScore_NoValidGoals_ReturnsNull()
    {
        var calculator = new ScorecardScoreCalculator();

        Assert.Null(calculator.Calculate(new List<ScorecardScoreContribution>()));
    }

    [Fact]
    public void TimeAggregator_Cumulative_Sums()
    {
        var result = TimeAggregator.Aggregate(new decimal?[] { 10m, 20m, null, 30m }, isCumulative: true);

        Assert.Equal(60m, result);
    }

    [Fact]
    public void TimeAggregator_NonCumulative_AveragesIgnoringNulls()
    {
        var result = TimeAggregator.Aggregate(new decimal?[] { 10m, 20m, null, 30m }, isCumulative: false);

        Assert.Equal(20m, result);
    }

    [Fact]
    public void TimeAggregator_AllNull_ReturnsNull()
    {
        var result = TimeAggregator.Aggregate(new decimal?[] { null, null }, isCumulative: false);

        Assert.Null(result);
    }
}
