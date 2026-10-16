namespace Domain.Calculation;

/// <summary>
/// Default <see cref="IInitiativeProgressCalculator"/>. Pure arithmetic with no dependencies,
/// so it is safe to register as a singleton and to unit test in isolation.
/// </summary>
public sealed class InitiativeProgressCalculator : IInitiativeProgressCalculator
{
    /// <summary>Weights are percentages and must total exactly 100 for the configuration to be valid (FR-402).</summary>
    public const decimal RequiredTotalWeight = 100m;

    private const decimal WeightTolerance = 0.0001m;

    public InitiativeProgressResult Calculate(IEnumerable<InitiativeProgressInput> milestones)
    {
        ArgumentNullException.ThrowIfNull(milestones);

        var contributions = new List<MilestoneContribution>();
        decimal totalWeight = 0m;
        decimal weighted = 0m;
        var hasNegativeWeight = false;

        foreach (var milestone in milestones)
        {
            var weight = milestone.Weight;
            var completion = milestone.CompletionPercentage;

            // Weights and completion are validated on write, but the calculator must never
            // emit a nonsensical percentage if bad data ever reaches it.
            if (weight < 0m)
            {
                hasNegativeWeight = true;
                weight = 0m;
            }

            if (completion < 0)
            {
                completion = 0;
            }
            else if (completion > 100)
            {
                completion = 100;
            }

            var contribution = weight * completion / 100m;

            contributions.Add(new MilestoneContribution(
                milestone.MilestoneId,
                weight,
                completion,
                Round(contribution)));

            totalWeight += weight;
            weighted += contribution;
        }

        if (contributions.Count == 0)
        {
            return InitiativeProgressResult.Empty;
        }

        // Progress is expressed against the FRD's 100-point weight scale. When the recorded
        // weights do not total 100 the figure is still reported (so a partially configured
        // initiative is not blank) but flagged as not properly configured.
        var progress = totalWeight > 0m
            ? Clamp(weighted / RequiredTotalWeight * 100m)
            : 0m;

        return new InitiativeProgressResult(
            Round(progress),
            Round(totalWeight),
            HasMilestones: true,
            WeightsAreValid: !hasNegativeWeight && Math.Abs(totalWeight - RequiredTotalWeight) <= WeightTolerance,
            Contributions: contributions);
    }

    public bool WeightsTotalOneHundred(IEnumerable<InitiativeProgressInput> milestones, decimal tolerance = WeightTolerance)
    {
        return Math.Abs(TotalWeight(milestones) - RequiredTotalWeight) <= tolerance
            && !HasNegativeWeight(milestones);
    }

    public decimal TotalWeight(IEnumerable<InitiativeProgressInput> milestones)
    {
        ArgumentNullException.ThrowIfNull(milestones);

        var total = 0m;
        foreach (var milestone in milestones)
        {
            total += milestone.Weight;
        }

        return Round(total);
    }

    private static bool HasNegativeWeight(IEnumerable<InitiativeProgressInput> milestones)
    {
        foreach (var milestone in milestones)
        {
            if (milestone.Weight < 0m)
            {
                return true;
            }
        }

        return false;
    }

    private static decimal Clamp(decimal value) => value < 0m ? 0m : value > 100m ? 100m : value;

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
