using Application.Dashboard;
using Domain.Constants;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IService
{
    /// <summary>
    /// Single entry point for the effective RAG thresholds (FR-607). Consumers load thresholds once
    /// per logical operation and pass the result to <c>DashboardRag.FromScore</c> so every view bands
    /// a score identically. Thresholds must never be read row by row from a loop.
    /// </summary>
    public interface IRagThresholdService
    {
        /// <summary>Effective thresholds for a scope, falling back to FR-607 defaults when nothing is stored.</summary>
        Task<RagThresholds> GetEffectiveAsync(string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default);

        /// <summary>Full configuration detail (effective values, source, colours, warnings) for the admin API.</summary>
        Task<RagThresholdsConfiguration> GetConfigurationAsync(string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates and persists new thresholds for a scope, replacing the stored band rows.
        /// Throws <c>ValidationException</c> when the pair is invalid; persistence failures surface
        /// rather than being reported as a successful update.
        /// </summary>
        Task<RagThresholdsConfiguration> UpdateAsync(decimal green, decimal yellow, string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default);
    }
}
