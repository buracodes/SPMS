using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DivisionContributionRepository : GenericRepository<DivisionContribution>, IDivisionContributionRepository
{
    public DivisionContributionRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Guid>> GetGoalIdsByPlanAsync(Guid divisionPlanId)
    {
        return await context.DivisionContributions
            .Where(c => c.DivisionPlanId == divisionPlanId)
            .Select(c => c.GoalId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DivisionContribution>> GetByPlanAsync(Guid divisionPlanId)
    {
        return await context.DivisionContributions
            .Where(c => c.DivisionPlanId == divisionPlanId)
            .ToListAsync();
    }

    public async Task<bool> ExistsInPlanAsync(Guid divisionPlanId, Guid goalId)
    {
        return await context.DivisionContributions.AnyAsync(c => c.DivisionPlanId == divisionPlanId && c.GoalId == goalId);
    }
}
