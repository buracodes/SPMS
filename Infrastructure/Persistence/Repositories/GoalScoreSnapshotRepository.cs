using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class GoalScoreSnapshotRepository : GenericRepository<GoalScoreSnapshot>, IGoalScoreSnapshotRepository
{
    public GoalScoreSnapshotRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<GoalScoreSnapshot?> GetLatestAsync(Guid goalId, Guid fiscalPeriodId, SnapshotGranularity granularity)
    {
        return await context.GoalScoreSnapshots
            .AsNoTracking()
            .Where(s => s.GoalId == goalId && s.FiscalPeriodId == fiscalPeriodId && s.Granularity == granularity)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<GoalScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.GoalScoreSnapshots
            .AsNoTracking()
            .Where(s => s.ScorecardId == scorecardId && s.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .Include(s => s.FiscalPeriod)
            .OrderBy(s => s.FiscalPeriod!.FiscalMonth)
            .ThenBy(s => s.Granularity)
            .ThenByDescending(s => s.CalculatedAt)
            .ToListAsync();
    }
}
