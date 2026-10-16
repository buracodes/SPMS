using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KraScoreSnapshotRepository : GenericRepository<KraScoreSnapshot>, IKraScoreSnapshotRepository
{
    public KraScoreSnapshotRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<KraScoreSnapshot?> GetLatestAsync(Guid kraId, Guid fiscalPeriodId, SnapshotGranularity granularity)
    {
        return await context.KraScoreSnapshots
            .AsNoTracking()
            .Where(s => s.KraId == kraId && s.FiscalPeriodId == fiscalPeriodId && s.Granularity == granularity)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<KraScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.KraScoreSnapshots
            .AsNoTracking()
            .Where(s => s.ScorecardId == scorecardId && s.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .Include(s => s.FiscalPeriod)
            .OrderBy(s => s.FiscalPeriod!.FiscalMonth)
            .ThenBy(s => s.Granularity)
            .ThenByDescending(s => s.CalculatedAt)
            .ToListAsync();
    }
}
