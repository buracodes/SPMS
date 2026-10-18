using Domain.Calculation;
using Domain.Enums;
using Xunit;

namespace Application.Tests.Calculation;

/// <summary>
/// FR-602 regression guard: the KRA score is the weighted average of scored
/// LAGGING KPIs only. Leading KPIs and unscored KPIs are excluded, the eligible
/// weights are re-normalized, and KRA scores are NULL when nothing eligible
/// exists. Missing points never coerce to zero. These tests lock the contract
/// that the Corporate spine (Goal → KRA → KPI) depends on; the Division spine
/// (Goal → KPI, no KRA level) never invokes this calculator.
/// </summary>
public class KraScoreNormalizationTests
{
    private static KraScoreCalculator Calculator() => new();

    private static KraScoreContribution Lagging(decimal? score, decimal weight)
        => new(score, weight, KpiType.Lagging);

    private static KraScoreContribution Leading(decimal? score, decimal weight)
        => new(score, weight, KpiType.Leading);

    [Fact]
    public void FrdWorkedExample_Returns82_5()
    {
        // FR-602: A(lag, 90/50), B(lag, 70/30), C(leading, 80/20) → 82.5.
        var score = Calculator().Calculate(new[]
        {
            Lagging(90m, 50m),
            Lagging(70m, 30m),
            Leading(80m, 20m),
        });

        Assert.Equal(82.5m, score);
    }

    [Fact]
    public void LeadingKpis_AreExcluded_EventuallyWhenScored()
    {
        var score = Calculator().Calculate(new[]
        {
            Lagging(90m, 100m),
            Leading(0m, 100m),
        });

        Assert.Equal(90m, score);
    }

    [Fact]
    public void UnscoredLaggingKpis_AreExcluded_AndWeightsRenormalized()
    {
        // Lagging A scored, lagging B without a snapshot, lagging C scored:
        // B must not drag the average or coerce to zero.
        var score = Calculator().Calculate(new[]
        {
            Lagging(60m, 50m),
            Lagging(null, 50m),
            Lagging(100m, 50m),
        });

        // Eligible contributions 60@50 and 100@50 → plain 80.
        Assert.Equal(80m, score);
    }

    [Fact]
    public void Weights_DontSumTo100_AreNormalized()
    {
        var score = Calculator().Calculate(new[]
        {
            Lagging(90m, 50m),
            Lagging(70m, 30m),
        });

        // (90×50 + 70×30) / 80 = 6600/80 = 82.5.
        Assert.Equal(82.5m, score);
    }

    [Fact]
    public void NoEligibleScoredLaggingKpis_ReturnsNull()
    {
        Assert.Null(Calculator().Calculate(new[]
        {
            Leading(100m, 100m),
        }));

        Assert.Null(Calculator().Calculate(new[]
        {
            Lagging(null, 50m),
            Lagging(null, 50m),
        }));

        Assert.Null(Calculator().Calculate(Array.Empty<KraScoreContribution>()));
    }

    [Fact]
    public void Normalization_DoesNotMutateStoredKpiWeights()
    {
        // Normalization happens in-flight; the persisted KRA/KPI weights are never
        // rewritten as a side effect of a calculation.
        var contributions = new[]
        {
            Lagging(90m, 50m),
            Lagging(70m, 30m),
            Leading(80m, 20m),
        };
        var original = contributions.Select(c => c.Weight).ToArray();

        Calculator().Calculate(contributions);

        Assert.Equal(original, contributions.Select(c => c.Weight).ToArray());
    }
}
