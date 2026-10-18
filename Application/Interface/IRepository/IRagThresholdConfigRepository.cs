using Domain.Entities;
using Domain.Enums;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IRepository
{
    /// <summary>
    /// Reads and stages <see cref="RagThresholdConfig"/> rows (FR-607 configurable thresholds).
    /// Writes are staged and committed through <see cref="IUnitOfWork"/> so the band rows for a
    /// scope update in a single transaction.
    /// </summary>
    public interface IRagThresholdConfigRepository
    {
        Task<IReadOnlyList<RagThresholdConfig>> GetByScopeAsync(string scopeKey, CancellationToken cancellationToken = default);

        Task<RagThresholdConfig?> GetBandAsync(string scopeKey, RagBand band, CancellationToken cancellationToken = default);

        Task AddWithoutSave(RagThresholdConfig entity);

        Task UpdateWithoutSave(RagThresholdConfig entity);
    }
}
