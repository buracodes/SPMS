namespace Domain.Calculation;

/// <summary>
/// Budget position of an initiative (FR-404).
/// <para>
/// Budget Variance = Planned Budget - Actual Expenditure<br/>
/// Budget Utilization = Actual Expenditure / Planned Budget x 100
/// </para>
/// </summary>
/// <param name="IsBudgetTracked">False when no planned budget is configured (planned budget is zero).</param>
/// <param name="PlannedBudget">Planned budget as configured.</param>
/// <param name="ActualExpenditure">Actual expenditure as recorded.</param>
/// <param name="BudgetVariance">Planned - Actual. Negative means overspend. Null when no budget is tracked.</param>
/// <param name="BudgetUtilizationPercent">Actual / Planned x 100, or null when no budget is tracked.</param>
/// <param name="IsOverBudget">True when actual expenditure exceeds the planned budget.</param>
public sealed record InitiativeBudgetResult(
    bool IsBudgetTracked,
    decimal PlannedBudget,
    decimal ActualExpenditure,
    decimal? BudgetVariance,
    decimal? BudgetUtilizationPercent,
    bool IsOverBudget)
{
    public static InitiativeBudgetResult None { get; } =
        new(false, 0m, 0m, null, null, false);
}

/// <summary>
/// The single authoritative implementation of the FR-404 budget formulas. Guarded against
/// divide-by-zero, missing budgets and negative values.
/// </summary>
public interface IInitiativeBudgetCalculator
{
    InitiativeBudgetResult Calculate(decimal plannedBudget, decimal actualExpenditure);
}
