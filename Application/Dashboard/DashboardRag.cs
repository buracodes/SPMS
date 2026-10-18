using Domain.Constants;

namespace Application.Dashboard;

/// <summary>
/// RAG banding for dashboard, report and planning payloads (FR-607, FR-908). Thresholds are
/// administrator configurable through <c>RagThresholdConfig</c> / <c>IRagThresholdService</c>; the
/// constants here are the FR-607 fallback defaults only. Pass the effective <see cref="RagThresholds"/>
/// loaded once per operation so every view bands a score identically.
/// </summary>
public static class DashboardRag
{
    /// <summary>Fallback Green threshold. Effective value comes from <see cref="RagThresholds"/>.</summary>
    public const decimal GreenThreshold = RagThresholdDefaults.GreenThreshold;

    /// <summary>Fallback Yellow threshold. Effective value comes from <see cref="RagThresholds"/>.</summary>
    public const decimal YellowThreshold = RagThresholdDefaults.YellowThreshold;

    // Wire/UI tokens. These are the FE display dialect ("Green" | "Amber" | "Red" | "NoData") so
    // payloads need no remapping in Angular; the Yellow band is communicated as "Amber" and a
    // missing/unusable score as "NoData" - it is never reported as Red.
    public const string Green = "Green";
    public const string Amber = "Amber";
    public const string Red = "Red";
    public const string NoData = "NoData";

    // FRD vocabulary aliases: the UI labels the bands Yellow and Gray ("No score"), but the wire
    // values stay Amber/NoData so the API contract and existing clients keep working.
    public const string Yellow = Amber;
    public const string Gray = NoData;

    public const decimal AmberThreshold = YellowThreshold;

    /// <summary>Human readable band for a score using FR-607 defaults, or <see cref="NoData"/> when no score exists.</summary>
    public static string FromScore(decimal? score) => RagThresholds.Default.Classify(score);

    /// <summary>Human readable band for a score using the configured thresholds, or <see cref="NoData"/> when no score exists.</summary>
    public static string FromScore(decimal? score, RagThresholds? thresholds)
        => (thresholds ?? RagThresholds.Default).Classify(score);

    /// <summary>Red only when a score exists and falls below the Yellow threshold (FR-607 defaults).</summary>
    public static bool IsRed(decimal? score) => RagThresholds.Default.IsRed(score);

    /// <summary>Red only when a score exists and falls below the configured Yellow threshold.</summary>
    public static bool IsRed(decimal? score, RagThresholds? thresholds)
        => (thresholds ?? RagThresholds.Default).IsRed(score);
}
