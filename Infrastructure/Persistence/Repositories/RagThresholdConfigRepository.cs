using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Repository for <see cref="RagThresholdConfig"/> (FR-607 configurable RAG thresholds).
    /// Writes are staged only; callers commit through <c>IUnitOfWork.SaveChangesAsync</c> so all
    /// band rows for a scope land in one transaction.
    /// </summary>
    public class RagThresholdConfigRepository : IRagThresholdConfigRepository
    {
        private readonly SpmsDbContext context;

        public RagThresholdConfigRepository(SpmsDbContext _context)
        {
            context = _context;
        }

        public async Task<IReadOnlyList<RagThresholdConfig>> GetByScopeAsync(string scopeKey, CancellationToken cancellationToken = default)
        {
            // Tracked on purpose: UpdateAsync mutates these rows and re-persists them, so detached
            // copies would collide with still-tracked instances when a scope is updated twice in
            // one request scope.
            return await context.RagThresholdConfigs
                .Where(x => x.ScopeKey == scopeKey)
                .OrderBy(x => x.Band)
                .ToListAsync(cancellationToken);
        }

        public async Task<RagThresholdConfig?> GetBandAsync(string scopeKey, RagBand band, CancellationToken cancellationToken = default)
        {
            return await context.RagThresholdConfigs
                .FirstOrDefaultAsync(x => x.ScopeKey == scopeKey && x.Band == band, cancellationToken);
        }

        public async Task AddWithoutSave(RagThresholdConfig entity)
        {
            await context.RagThresholdConfigs.AddAsync(entity);
        }

        public Task UpdateWithoutSave(RagThresholdConfig entity)
        {
            context.RagThresholdConfigs.Update(entity);
            return Task.CompletedTask;
        }
    }
}
