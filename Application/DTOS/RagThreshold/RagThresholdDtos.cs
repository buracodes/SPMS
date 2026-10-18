using System.Collections.Generic;

namespace Application.DTOS.RagThreshold
{
    /// <summary>Effective RAG thresholds returned to the admin screen and to every client that bands scores.</summary>
    public class RagThresholdsDto
    {
        public string ScopeKey { get; set; } = "default";
        public decimal Green { get; set; } = 95;
        public decimal Yellow { get; set; } = 70;
        public bool IsConfigured { get; set; }
        public string Source { get; set; } = "Defaults";
        public string GreenColor { get; set; } = string.Empty;
        public string YellowColor { get; set; } = string.Empty;
        public string RedColor { get; set; } = string.Empty;
        public string GrayColor { get; set; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>Payload for updating the effective thresholds (FR-607).</summary>
    public class UpdateRagThresholdsDto
    {
        public decimal Green { get; set; } = 95;
        public decimal Yellow { get; set; } = 70;
    }
}
