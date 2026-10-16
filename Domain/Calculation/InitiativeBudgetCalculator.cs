namespace Domain.Calculation;

/// <summary>
/// Default <see cref="IInitiativeBudgetCalculator"/> (FR-404).
/// <para>
/// A planned budget of zero (or less) means budget tracking is not configured, so no
/// utilisation percentage is produced and no division is attempted. Negative actual
/// expenditure is treated as zero so historical bad data can never inflate utilisation.
/// </para>
/// </summary>
public sealed class InitiativeBudgetCalculator : IInitiativeBudgetCalculator
{
    public InitiativeBudgetResult Calculate(decimal plannedBudget, decimal actualExpenditure)
    {
        var planned = plannedBudget < 0m ? 0m : plannedBudget;
        var actual = actualExpenditure < 0m ? 0m : actualExpenditure;

        // FR-404: no planned budget configured -> nothing to report, and no divide-by-zero.
        if (planned <= 0m)
        {
            return InitiativeBudgetResult.None with
            {
                PlannedBudget = planned,
                ActualExpenditure = actual,
                BudgetVariance = null,
                BudgetUtilizationPercent = null
            };
        }

        var variance = planned - actual;
        var utilization = Math.Round(actual / planned * 100m, 2, MidpointRounding.AwayFromZero);

        return new InitiativeBudgetResult(
            IsBudgetTracked: true,
            PlannedBudget: planned,
            ActualExpenditure: actual,
            BudgetVariance: variance,
            BudgetUtilizationPercent: utilization,
            IsOverBudget: variance < 0m);
    }
}
