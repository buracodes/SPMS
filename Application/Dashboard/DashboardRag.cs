namespace Application.Dashboard;

/// <summary>
/// RAG banding for dashboard payloads. The thresholds intentionally mirror
/// <c>ReportingService.Rag</c> so the dashboard and the report engine always agree
/// on whether a score is on track, at risk or off track.
/// </summary>
public static class DashboardRag
{
    public const decimal GreenThreshold = 95m;
    public const decimal AmberThreshold = 70m;

    public const string Green = "Green";
    public const string Amber = "Amber";
    public const string Red = "Red";
    public const string NoData = "NoData";

    /// <summary>Human readable band for a score, or <see cref="NoData"/> when no score exists.</summary>
    public static string FromScore(decimal? score)
    {
        if (!score.HasValue)
        {
            return NoData;
        }

        if (score.Value >= GreenThreshold)
        {
            return Green;
        }

        return score.Value >= AmberThreshold ? Amber : Red;
    }

    public static bool IsRed(decimal? score) => score.HasValue && score.Value < AmberThreshold;
}
