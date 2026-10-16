using Domain.Calculation;

namespace Application.Tests.Initiatives;

/// <summary>FR-404 budget variance, utilisation and the "no budget configured" case.</summary>
public class InitiativeBudgetCalculatorTests
{
    private readonly InitiativeBudgetCalculator calculator = new();

    [Fact]
    public void Calculate_UnderBudget_ReportsPositiveVarianceAndUtilisation()
    {
        var result = calculator.Calculate(1000m, 400m);

        Assert.True(result.IsBudgetTracked);
        Assert.Equal(600m, result.BudgetVariance);
        Assert.Equal(40m, result.BudgetUtilizationPercent);
        Assert.False(result.IsOverBudget);
    }

    [Fact]
    public void Calculate_ExactlyOnBudget_ReportsZeroVariance()
    {
        var result = calculator.Calculate(750m, 750m);

        Assert.Equal(0m, result.BudgetVariance);
        Assert.Equal(100m, result.BudgetUtilizationPercent);
        Assert.False(result.IsOverBudget);
    }

    [Fact]
    public void Calculate_OverBudget_ReportsNegativeVariance()
    {
        var result = calculator.Calculate(500m, 620.5m);

        Assert.Equal(-120.5m, result.BudgetVariance);
        Assert.Equal(124.1m, result.BudgetUtilizationPercent);
        Assert.True(result.IsOverBudget);
    }

    [Fact]
    public void Calculate_NoPlannedBudget_IsNotTrackedAndDoesNotDivide()
    {
        var result = calculator.Calculate(0m, 250m);

        Assert.False(result.IsBudgetTracked);
        // Both derived figures are null rather than zero: "no budget configured" must never read
        // as "0% spent".
        Assert.Null(result.BudgetUtilizationPercent);
        Assert.Null(result.BudgetVariance);
        Assert.False(result.IsOverBudget);
        Assert.Equal(0m, result.PlannedBudget);
        Assert.Equal(250m, result.ActualExpenditure);
    }

    [Fact]
    public void Calculate_NegativeActualExpenditure_IsTreatedAsZero()
    {
        var result = calculator.Calculate(200m, -50m);

        Assert.Equal(0m, result.ActualExpenditure);
        Assert.Equal(200m, result.PlannedBudget);
        Assert.Equal(0m, result.BudgetUtilizationPercent);
        Assert.False(result.IsOverBudget);
    }

    [Fact]
    public void Calculate_NegativePlannedBudget_IsNotTracked()
    {
        var result = calculator.Calculate(-100m, 10m);

        Assert.False(result.IsBudgetTracked);
        Assert.Equal(0m, result.PlannedBudget);
        Assert.Null(result.BudgetUtilizationPercent);
    }

    [Fact]
    public void Calculate_Utilisation_IsRoundedToTwoDecimals()
    {
        var result = calculator.Calculate(3m, 1m);

        Assert.Equal(33.33m, result.BudgetUtilizationPercent);
    }
}