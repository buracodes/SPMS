using System.Text.Json.Serialization;
using Domain.Entities;

namespace Application.DTOS.KpiTarget
{
    public class KpiTargetValueDto
    {
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }

        /// <summary>
        /// Serialized as the exact enum name (e.g. "Monthly", "Quarterly", "Annual")
        /// to match the granularity contract consumed by the frontend.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TargetGranularity Granularity { get; set; }
        public decimal TargetValue { get; set; }

        /// <summary>
        /// Set on a quarterly row that was computed from its monthly targets, so the editor
        /// reopens in auto-calculate mode. Always false on monthly and annual rows.
        /// </summary>
        public bool IsDerivedFromMonthly { get; set; }
    }
}