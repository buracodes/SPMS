using Domain.Constants;
using System.Collections.Generic;
using System.Linq;

namespace Application.Dashboard;

/// <summary>
/// Effective RAG thresholds (FR-607). One immutable value per logical operation so the dashboard,
/// reports, strategic scorecard, KPI details, score planning and division planning band the same
/// score identically. Classification lives here - this is the single authoritative evaluator.
/// </summary>
public sealed record RagThresholds
{
    public decimal Green { get; init; } = RagThresholdDefaults.GreenThreshold;
    public decimal Yellow { get; init; } = RagThresholdDefaults.YellowThreshold;

    /// <summary>FR-607 defaults; used when no configuration is stored for the scope.</summary>
    public static RagThresholds Default { get; } = new();

    /// <summary>
    /// Validates a threshold pair against the supported score range and the FRD rule that the
    /// Yellow band sits strictly below the Green band. Returns human readable problems; empty means valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(decimal green, decimal yellow)
    {
        var errors = new List<string>();

        if (green < RagThresholdDefaults.MinSupportedScore || green > RagThresholdDefaults.MaxSupportedScore)
        {
            errors.Add($"Green threshold must be between {RagThresholdDefaults.MinSupportedScore} and {RagThresholdDefaults.MaxSupportedScore}.");
        }

        if (yellow < RagThresholdDefaults.MinSupportedScore || yellow > RagThresholdDefaults.MaxSupportedScore)
        {
            errors.Add($"Yellow threshold must be between {RagThresholdDefaults.MinSupportedScore} and {RagThresholdDefaults.MaxSupportedScore}.");
        }

        if (errors.Count == 0 && yellow >= green)
        {
            errors.Add("Yellow threshold must be strictly lower than the Green threshold.");
        }

        return errors;
    }

    /// <summary>Creates a valid threshold pair, or <c>null</c> when validation fails.</summary>
    public static RagThresholds? TryCreate(decimal green, decimal yellow)
    {
        return Validate(green, yellow).Count == 0
            ? new RagThresholds { Green = green, Yellow = yellow }
            : null;
    }

    /// <summary>
    /// FR-607 banding for a derived score: Green when score >= Green threshold, Yellow when
    /// score >= Yellow threshold, Red below it. A missing score is always Gray - it is never
    /// coerced to 0 and never reported as Red.
    /// </summary>
    public string Classify(decimal? score)
    {
        if (!score.HasValue)
        {
            return DashboardRag.NoData;
        }

        if (score.Value >= Green)
        {
            return DashboardRag.Green;
        }

        return score.Value >= Yellow ? DashboardRag.Amber : DashboardRag.Red;
    }

    /// <summary>Red only when a score exists and falls below the Yellow threshold.</summary>
    public bool IsRed(decimal? score) => score.HasValue && score.Value < Yellow;
}
