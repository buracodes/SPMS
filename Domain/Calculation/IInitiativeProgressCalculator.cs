namespace Domain.Calculation;

/// <summary>Weighted contribution of a single milestone to its initiative's progress.</summary>
/// <param name="MilestoneId">Milestone the contribution belongs to.</param>
/// <param name="Weight">Milestone weight (0-100).</param>
/// <param name="CompletionPercentage">Milestone completion (0-100).</param>
/// <param name="ContributionPercentage">Weight x Completion / 100.</param>
public sealed record MilestoneContribution(
    Guid MilestoneId,
    decimal Weight,
    int CompletionPercentage,
    decimal ContributionPercentage);

/// <summary>Result of calculating an initiative's weighted progress (FR-402/FR-403).</summary>
/// <param name="ProgressPercentage">Backend-calculated weighted progress, 0-100.</param>
/// <param name="TotalWeight">Sum of the recorded milestone weights (should be 100).</param>
/// <param name="HasMilestones">False when the initiative has no milestones at all.</param>
/// <param name="WeightsAreValid">True only when the weights total exactly 100.</param>
/// <param name="Contributions">Per-milestone breakdown for the detail view.</param>
public sealed record InitiativeProgressResult(
    decimal ProgressPercentage,
    decimal TotalWeight,
    bool HasMilestones,
    bool WeightsAreValid,
    IReadOnlyList<MilestoneContribution> Contributions)
{
    public static InitiativeProgressResult Empty { get; } =
        new(0m, 0m, false, true, Array.Empty<MilestoneContribution>());
}

/// <summary>
/// The single authoritative implementation of the FRD weighted-progress formula:
/// <code>Initiative Progress = SUM(Milestone Weight x Milestone Completion %) / 100</code>.
/// Every consumer (queries, reports, dashboards, closure rules) must use this rather than
/// re-implementing the arithmetic.
/// </summary>
public interface IInitiativeProgressCalculator
{
    InitiativeProgressResult Calculate(IEnumerable<InitiativeProgressInput> milestones);

    /// <summary>True when the supplied milestone weights total exactly 100.</summary>
    bool WeightsTotalOneHundred(IEnumerable<InitiativeProgressInput> milestones, decimal tolerance = 0.0001m);

    /// <summary>Sum of the supplied milestone weights, for reporting the configured total.</summary>
    decimal TotalWeight(IEnumerable<InitiativeProgressInput> milestones);
}

/// <summary>Minimal milestone shape the calculator needs, so it can be fed from EF projections.</summary>
public readonly record struct InitiativeProgressInput(
    Guid MilestoneId,
    decimal Weight,
    int CompletionPercentage);
