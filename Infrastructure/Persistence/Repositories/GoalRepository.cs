using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class GoalRepository : GenericRepository<Goal>, IGoalRepository
{
    public GoalRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<Goal?> GetByIdWithKrasAsync(Guid id)
    {
        return await context.Goals
            .Include(g => g.Kras)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<IReadOnlyList<Goal>> GetByScorecardAsync(Guid scorecardId)
    {
        return await context.Goals
            .Where(g => g.ScorecardId == scorecardId)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Guid>> GetGoalIdsByScorecardAsync(Guid scorecardId)
    {
        return await context.Goals
            .Where(g => g.ScorecardId == scorecardId)
            .Select(g => g.Id)
            .ToListAsync();
    }

    public async Task<decimal> GetWeightsSumAsync(Guid scorecardId, Guid? excludeGoalId = null, CancellationToken cancellationToken = default)
    {
        // The global !IsDeleted query filter keeps soft-deleted rows out of the budget,
        // matching every other weight-budget guard (milestones, division goals).
        var query = context.Goals.Where(g => g.ScorecardId == scorecardId);
        if (excludeGoalId.HasValue)
        {
            query = query.Where(g => g.Id != excludeGoalId.Value);
        }

        return await query.SumAsync(g => g.Weight, cancellationToken);
    }

    public async Task<bool> HasAnyKrasIncludingDeletedAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters deliberately: deletion-integrity must also detect
        // soft-deleted KRAs that normal reads (and the Kras navigation) hide.
        return await context.Kras
            .IgnoreQueryFilters()
            .AnyAsync(k => k.GoalId == goalId, cancellationToken);
    }
}
