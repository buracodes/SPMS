using Domain.Calculation;

namespace Application.Tests.Initiatives;

/// <summary>
/// FR-402/FR-403: the single authoritative progress definition, so the API, grid, reports and
/// dashboards can never disagree with each other.
/// </summary>
public class InitiativeProgressCalculatorTests
{
    private readonly InitiativeProgressCalculator calculator = new();

    private static InitiativeProgressInput Milestone(decimal weight, int completion, int index = 0)
        => new(Guid.NewGuid(), weight, completion);

    [Fact]
    public void Calculate_WeightsTotalOneHundred_ReturnsWeightedAverage()
    {
        var result = calculator.Calculate(new[]
        {
            Milestone(40m, 100),
            Milestone(30m, 50),
            Milestone(30m, 0)
        });

        // (40*100 + 30*50 + 30*0) / 100
        Assert.Equal(55m, result.ProgressPercentage);
        Assert.Equal(100m, result.TotalWeight);
        Assert.True(result.WeightsAreValid);
        Assert.True(result.HasMilestones);
    }

    [Fact]
    public void Calculate_AllMilestonesComplete_Returns100()
    {
        var result = calculator.Calculate(new[]
        {
            Milestone(25m, 100),
            Milestone(75m, 100)
        });

        Assert.Equal(100m, result.ProgressPercentage);
        Assert.True(result.WeightsAreValid);
    }

    [Fact]
    public void Calculate_NoProgressMade_ReturnsZero()
    {
        var result = calculator.Calculate(new[]
        {
            Milestone(60m, 0),
            Milestone(40m, 0)
        });

        Assert.Equal(0m, result.ProgressPercentage);
    }

    [Fact]
    public void Calculate_WeightsDoNotTotalOneHundred_FlagsNotConfigured()
    {
        // Weights sum to 90, so the plan is incomplete even though progress is reported.
        var result = calculator.Calculate(new[]
        {
            Milestone(50m, 100),
            Milestone(40m, 100)
        });

        Assert.False(result.WeightsAreValid);
        Assert.Equal(90m, result.TotalWeight);
        // Progress is still reported against the 100-point scale, so a partially configured
        // initiative is never blank.
        Assert.Equal(90m, result.ProgressPercentage);
    }

    [Fact]
    public void Calculate_WeightsSlightlyOverOneHundred_IsStillConfigured()
    {
        var result = calculator.Calculate(new[]
        {
            Milestone(60m, 100),
            Milestone(40m, 100)
        });

        Assert.True(result.WeightsAreValid);
    }

    [Fact]
    public void Calculate_NoMilestones_ReturnsEmptyResult()
    {
        var result = calculator.Calculate(Array.Empty<InitiativeProgressInput>());

        Assert.False(result.HasMilestones);
        Assert.Equal(0m, result.ProgressPercentage);
        Assert.Empty(result.Contributions);
    }

    [Fact]
    public void Calculate_CompletionOutsideBounds_IsClamped()
    {
        var result = calculator.Calculate(new[]
        {
            Milestone(100m, 140),
            Milestone(0m, -20)
        });

        Assert.Equal(100m, result.ProgressPercentage);
        Assert.DoesNotContain(result.Contributions, c => c.CompletionPercentage > 100);
        Assert.DoesNotContain(result.Contributions, c => c.CompletionPercentage < 0);
    }

    [Fact]
    public void Calculate_NegativeWeight_IsTreatedAsInvalidAndClamped()
    {
        var result = calculator.Calculate(new[]
        {
            Milestone(120m, 100),
            Milestone(-20m, 100)
        });

        Assert.False(result.WeightsAreValid);
        Assert.True(result.ProgressPercentage >= 0m);
    }

    [Fact]
    public void Calculate_ExcessWeight_DoesNotExceedOneHundredPercent()
    {
        // Over-weighted data can never report more than 100% progress.
        var result = calculator.Calculate(new[]
        {
            Milestone(150m, 100),
            Milestone(100m, 100)
        });

        Assert.Equal(100m, result.ProgressPercentage);
    }

    [Fact]
    public void Calculate_ReportsPerMilestoneContribution()
    {
        var first = Milestone(25m, 80);
        var second = Milestone(75m, 20);

        var result = calculator.Calculate(new[] { first, second });

        Assert.Equal(2, result.Contributions.Count);
        Assert.Equal(20m, result.Contributions.Single(c => c.MilestoneId == first.MilestoneId).ContributionPercentage);
        Assert.Equal(15m, result.Contributions.Single(c => c.MilestoneId == second.MilestoneId).ContributionPercentage);
        Assert.Equal(35m, result.Contributions.Sum(c => c.ContributionPercentage));
    }

    [Fact]
    public void WeightsTotalOneHundred_ToleratesFloatingPointNoise()
    {
        Assert.True(calculator.WeightsTotalOneHundred(new[]
        {
            Milestone(33.33m, 0),
            Milestone(33.33m, 0),
            Milestone(33.34m, 0)
        }));
    }

    [Fact]
    public void WeightsTotalOneHundred_RejectsShortfall()
    {
        Assert.False(calculator.WeightsTotalOneHundred(new[] { Milestone(90m, 0) }));
    }

    [Fact]
    public void TotalWeight_SumsMilestoneWeights()
    {
        Assert.Equal(100m, calculator.TotalWeight(new[]
        {
            Milestone(10m, 0),
            Milestone(45.5m, 0),
            Milestone(44.5m, 0)
        }));
    }
}