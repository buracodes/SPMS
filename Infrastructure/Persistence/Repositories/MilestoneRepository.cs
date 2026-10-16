using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class MilestoneRepository : GenericRepository<Milestone>, IMilestoneRepository
{
    public MilestoneRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Milestone>> GetByInitiativeIdAsync(Guid initiativeId)
    {
        return await context.Milestones
            .Where(m => m.InitiativeId == initiativeId)
            .OrderBy(m => m.DueDate)
            .ThenBy(m => m.Name)
            .ToListAsync();
    }

    public async Task<Milestone?> GetByIdForInitiativeAsync(Guid milestoneId, Guid initiativeId)
    {
        return await context.Milestones
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.InitiativeId == initiativeId);
    }

    public async Task<bool> ExistsByNameAsync(Guid initiativeId, string name, Guid? excludeId = null)
    {
        var normalized = name.Trim();
        return await context.Milestones
            .AnyAsync(m => m.InitiativeId == initiativeId
                && m.Id != excludeId
                && m.Name == normalized);
    }

    public async Task<decimal> GetTotalWeightAsync(Guid initiativeId, Guid? excludeMilestoneId = null)
    {
        var query = context.Milestones.Where(m => m.InitiativeId == initiativeId);

        if (excludeMilestoneId.HasValue)
        {
            query = query.Where(m => m.Id != excludeMilestoneId.Value);
        }

        return await query.SumAsync(m => m.Weight);
    }
}
