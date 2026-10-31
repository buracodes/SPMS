using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DivisionGoalRepository : GenericRepository<DivisionGoal>, IDivisionGoalRepository
{
    public DivisionGoalRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<DivisionGoal?> GetByIdWithPlanAsync(Guid id)
    {
        return await context.DivisionGoals
            .Include(g => g.DivisionPlan)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<IReadOnlyList<DivisionGoal>> GetByPlanWithKpisAsync(Guid divisionPlanId)
    {
        return await context.DivisionGoals
            .Where(g => g.DivisionPlanId == divisionPlanId)
            .Include(g => g.Kpis)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DivisionGoal>> GetByPlanIdsAsync(IReadOnlyList<Guid> divisionPlanIds)
    {
        return await context.DivisionGoals
            .Where(g => divisionPlanIds.Contains(g.DivisionPlanId))
            .Select(g => new DivisionGoal { Id = g.Id, DivisionPlanId = g.DivisionPlanId, Name = string.Empty })
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DivisionGoal>> GetByPlanIdsWithKpisAsync(IReadOnlyList<Guid> divisionPlanIds)
    {
        return await context.DivisionGoals
            .Where(g => divisionPlanIds.Contains(g.DivisionPlanId))
            .Include(g => g.Kpis)
            .Include(g => g.ParentGoal)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<bool> ExistsByNameInPlanAsync(Guid divisionPlanId, string name, Guid? excludeId = null)
    {
        return await context.DivisionGoals.AnyAsync(g => g.DivisionPlanId == divisionPlanId && g.Name == name && g.Id != excludeId);
    }

    public async Task<bool> ExistsUnderParentGoalsAsync(Guid divisionPlanId, IReadOnlyList<Guid> parentGoalIds)
    {
        return await context.DivisionGoals.AnyAsync(g => g.DivisionPlanId == divisionPlanId && parentGoalIds.Contains(g.ParentGoalId));
    }

    public async Task<decimal> GetWeightsSumAsync(Guid divisionPlanId, Guid? excludeGoalId = null)
    {
        var query = context.DivisionGoals.Where(g => g.DivisionPlanId == divisionPlanId);
        if (excludeGoalId.HasValue)
        {
            query = query.Where(g => g.Id != excludeGoalId.Value);
        }
        return await query.SumAsync(g => g.Weight);
    }
}
