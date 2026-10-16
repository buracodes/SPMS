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
}
