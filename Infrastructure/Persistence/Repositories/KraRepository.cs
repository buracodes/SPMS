using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KraRepository : GenericRepository<Kra>, IKraRepository
{
    public KraRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<Kra?> GetByIdWithKpisAsync(Guid id)
    {
        return await context.Kras
            .Include(k => k.Kpis)
            .FirstOrDefaultAsync(k => k.Id == id);
    }

    public async Task<decimal> GetWeightsSumAsync(Guid goalId, Guid? excludeKraId = null, CancellationToken cancellationToken = default)
    {
        // The global !IsDeleted query filter keeps soft-deleted rows out of the budget,
        // matching every other weight-budget guard (goals, milestones, division goals).
        var query = context.Kras.Where(k => k.GoalId == goalId);
        if (excludeKraId.HasValue)
        {
            query = query.Where(k => k.Id != excludeKraId.Value);
        }

        return await query.SumAsync(k => k.Weight, cancellationToken);
    }

    public async Task<bool> HasKpisIncludingDeletedAsync(Guid kraId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters deliberately: deletion-integrity must also detect
        // soft-deleted KPIs that normal reads (and the Kpis navigation) hide.
        return await context.Kpis
            .IgnoreQueryFilters()
            .AnyAsync(k => k.KraId == kraId, cancellationToken);
    }
}
