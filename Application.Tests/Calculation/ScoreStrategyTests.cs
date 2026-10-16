using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Enums;

namespace Application.Tests.Calculation;

public class ScoreStrategyTests
{
    private readonly ScoreStrategyFactory factory = new();

    [Theory]
    [InlineData(90, 100, 90)]
    [InlineData(110, 100, 110)]
    [InlineData(50, 100, 50)]
    [InlineData(0, 100, 0)]
    public void Maximize_IsActualOverTargetTimes100(decimal actual, decimal target, decimal expected)
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Maximize);

        var score = strategy.Score(new KpiScoreRequest(actual, target, KpiOptimizationType.Maximize));

        Assert.Equal(expected, score);
    }

    [Fact]
    public void Maximize_TargetZero_ReturnsNull()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Maximize);

        Assert.Null(strategy.Score(new KpiScoreRequest(50, 0, KpiOptimizationType.Maximize)));
    }

    [Theory]
    [InlineData(80, 100, 125)]
    [InlineData(100, 100, 100)]
    [InlineData(200, 100, 50)]
    [InlineData(0, 100, 0)]
    public void Minimize_IsTargetOverActualTimes100(decimal actual, decimal target, decimal expected)
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Minimize);

        var score = strategy.Score(new KpiScoreRequest(actual, target, KpiOptimizationType.Minimize));

        Assert.Equal(expected, score);
    }

    [Fact]
    public void Minimize_TargetZero_ReturnsNull()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Minimize);

        Assert.Null(strategy.Score(new KpiScoreRequest(50, 0, KpiOptimizationType.Minimize)));
    }

    [Fact]
    public void ExactHit_ExactMatch_Scores100()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.ExactHit);

        Assert.Equal(100m, strategy.Score(new KpiScoreRequest(42, 42, KpiOptimizationType.ExactHit)));
    }

    [Fact]
    public void ExactHit_Miss_Scores0()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.ExactHit);

        Assert.Equal(0m, strategy.Score(new KpiScoreRequest(41, 42, KpiOptimizationType.ExactHit)));
    }

    [Fact]
    public void ExactHit_WithinTolerance_Scores100()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.ExactHit);

        Assert.Equal(100m, strategy.Score(new KpiScoreRequest(43, 42, KpiOptimizationType.ExactHit, 5m)));
    }

    [Fact]
    public void ExactHit_OutsideTolerance_Scores0()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.ExactHit);

        Assert.Equal(0m, strategy.Score(new KpiScoreRequest(45, 42, KpiOptimizationType.ExactHit, 5m)));
    }

    [Fact]
    public void Binary_Achieved_Scores100()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Binary);

        Assert.Equal(100m, strategy.Score(new KpiScoreRequest(1, 1, KpiOptimizationType.Binary)));
    }

    [Fact]
    public void Binary_NotAchieved_Scores0()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Binary);

        Assert.Equal(0m, strategy.Score(new KpiScoreRequest(0, 1, KpiOptimizationType.Binary)));
    }

    [Fact]
    public void Binary_TargetZero_ReturnsNull()
    {
        var strategy = factory.GetStrategy(KpiOptimizationType.Binary);

        Assert.Null(strategy.Score(new KpiScoreRequest(1, 0, KpiOptimizationType.Binary)));
    }
}
