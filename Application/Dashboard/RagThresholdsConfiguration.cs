using Domain.Constants;
using System;
using System.Collections.Generic;

namespace Application.Dashboard;

/// <summary>
/// Admin-facing view of the effective RAG threshold configuration for a scope: the values in force,
/// where they came from, the band colours and any problems found in stored configuration.
/// </summary>
public sealed class RagThresholdsConfiguration
{
    public required string ScopeKey { get; init; }

    public decimal Green { get; init; } = RagThresholdDefaults.GreenThreshold;

    public decimal Yellow { get; init; } = RagThresholdDefaults.YellowThreshold;

    /// <summary>True when configuration rows exist for the scope (even if they are unusable).</summary>
    public bool IsConfigured { get; init; }

    /// <summary>"Database" when stored configuration drives the values, "Defaults" when FR-607 defaults do.</summary>
    public string Source { get; init; } = RagThresholdSource.Defaults;

    public string GreenColor { get; init; } = RagThresholdDefaults.GreenColor;

    public string YellowColor { get; init; } = RagThresholdDefaults.YellowColor;

    public string RedColor { get; init; } = RagThresholdDefaults.RedColor;

    public string GrayColor { get; init; } = RagThresholdDefaults.GrayColor;

    /// <summary>Problems found in stored configuration; empty when configuration is healthy.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public RagThresholds Thresholds => new() { Green = Green, Yellow = Yellow };

    public string Classify(decimal? score) => Thresholds.Classify(score);
}

public static class RagThresholdSource
{
    public const string Database = "Database";
    public const string Defaults = "Defaults";
}
