using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DivisionPlanRepository : GenericRepository<DivisionPlan>, IDivisionPlanRepository
{
    public DivisionPlanRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<DivisionPlan?> GetByDivisionAndScorecardAsync(Guid divisionId, Guid scorecardId)
    {
        return await context.DivisionPlans
            .FirstOrDefaultAsync(p => p.DivisionId == divisionId && p.ScorecardId == scorecardId);
    }

    public async Task<DivisionPlan?> GetByDivisionAndScorecardWithDetailsAsync(Guid divisionId, Guid scorecardId)
    {
        return await context.DivisionPlans
            .Include(p => p.Contributions)
                .ThenInclude(c => c.Goal)
            .Include(p => p.Goals)
                .ThenInclude(g => g.Kpis)
                    .ThenInclude(k => k.DivisionOwner)
            .Include(p => p.Goals)
                .ThenInclude(g => g.ParentGoal)
            .FirstOrDefaultAsync(p => p.DivisionId == divisionId && p.ScorecardId == scorecardId);
    }

    public async Task<DivisionPlan?> GetFirstByScorecardWithDetailsAsync(Guid scorecardId)
    {
        return await context.DivisionPlans
            .Include(p => p.Division)
            .Include(p => p.Contributions)
                .ThenInclude(c => c.Goal)
            .Include(p => p.Goals)
                .ThenInclude(g => g.Kpis)
                    .ThenInclude(k => k.DivisionOwner)
            .Include(p => p.Goals)
                .ThenInclude(g => g.ParentGoal)
            .Where(p => p.ScorecardId == scorecardId)
            .OrderBy(p => p.CreatedDate)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<DivisionPlan>> GetByDivisionAsync(Guid divisionId)
    {
        return await context.DivisionPlans
            .Where(p => p.DivisionId == divisionId)
            .Include(p => p.Scorecard)
            .OrderBy(p => p.CreatedDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DivisionPlan>> GetByScorecardAsync(Guid scorecardId)
    {
        return await context.DivisionPlans
            .Where(p => p.ScorecardId == scorecardId)
            .Include(p => p.Division)
            .Include(p => p.Scorecard)
            .OrderBy(p => p.CreatedDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DivisionPlan>> GetByDivisionIdsAsync(IReadOnlyList<Guid> divisionIds)
    {
        return await context.DivisionPlans
            .Where(p => divisionIds.Contains(p.DivisionId))
            .OrderBy(p => p.CreatedDate)
            .ToListAsync();
    }
}
