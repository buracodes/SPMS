using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class InitiativeUpdateLogRepository : GenericRepository<InitiativeUpdateLog>, IInitiativeUpdateLogRepository
{
    public InitiativeUpdateLogRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<InitiativeUpdateLog>> GetForInitiativeAsync(Guid initiativeId, Guid? milestoneId = null)
    {
        var query = context.InitiativeUpdateLogs
            .AsNoTracking()
            .Where(l => l.InitiativeId == initiativeId);

        if (milestoneId.HasValue)
        {
            query = query.Where(l => l.MilestoneId == milestoneId.Value);
        }

        return await query
            .OrderByDescending(l => l.CreatedDate)
            .ToListAsync();
    }

    public async Task<InitiativeUpdateLog?> GetLatestForMilestoneAsync(Guid milestoneId)
    {
        return await context.InitiativeUpdateLogs
            .AsNoTracking()
            .Where(l => l.MilestoneId == milestoneId)
            .OrderByDescending(l => l.CreatedDate)
            .FirstOrDefaultAsync();
    }
}
