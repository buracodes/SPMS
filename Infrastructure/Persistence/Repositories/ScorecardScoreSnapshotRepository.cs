using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ScorecardScoreSnapshotRepository : GenericRepository<ScorecardScoreSnapshot>, IScorecardScoreSnapshotRepository
{
    public ScorecardScoreSnapshotRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<ScorecardScoreSnapshot?> GetLatestAsync(Guid scorecardId, Guid fiscalPeriodId, SnapshotGranularity granularity)
    {
        return await context.ScorecardScoreSnapshots
            .AsNoTracking()
            .Where(s => s.ScorecardId == scorecardId && s.FiscalPeriodId == fiscalPeriodId && s.Granularity == granularity)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<ScorecardScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.ScorecardScoreSnapshots
            .AsNoTracking()
            .Where(s => s.ScorecardId == scorecardId && s.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .Include(s => s.FiscalPeriod)
            .OrderBy(s => s.FiscalPeriod!.FiscalMonth)
            .ThenBy(s => s.Granularity)
            .ThenByDescending(s => s.CalculatedAt)
            .ToListAsync();
    }
}
