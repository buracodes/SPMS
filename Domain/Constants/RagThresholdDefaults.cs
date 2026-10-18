namespace Domain.Constants;

/// <summary>
/// FR-607 default RAG thresholds and band colours. These are the values applied when no
/// <c>RagThresholdConfig</c> rows exist for a scope, and the values seeded on first run so an
/// administrator always starts from the FRD bands (Green >= 95%, Yellow 70-94%, Red &lt; 70%).
/// </summary>
public static class RagThresholdDefaults
{
    /// <summary>Scope used by every view until per-scope thresholds are introduced.</summary>
    public const string ScopeKey = "default";

    public const decimal GreenThreshold = 95m;
    public const decimal YellowThreshold = 70m;

    public const string GreenColor = "#16A34A";
    public const string YellowColor = "#D97706";
    public const string RedColor = "#DC2626";
    public const string GrayColor = "#6B7280";

    /// <summary>Lower bound of the supported score range used when validating thresholds.</summary>
    public const decimal MinSupportedScore = 0m;

    /// <summary>Upper bound of the supported score range used when validating thresholds.</summary>
    public const decimal MaxSupportedScore = 100m;
}
