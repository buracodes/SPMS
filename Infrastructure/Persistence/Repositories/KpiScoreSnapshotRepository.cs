using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KpiScoreSnapshotRepository : GenericRepository<KpiScoreSnapshot>, IKpiScoreSnapshotRepository
{
    public KpiScoreSnapshotRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<KpiScoreSnapshot?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId, SnapshotGranularity granularity)
    {
        return await context.KpiScoreSnapshots
            .AsNoTracking()
            .Where(s => s.KpiId == kpiId && s.FiscalPeriodId == fiscalPeriodId && s.Granularity == granularity)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<KpiScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.KpiScoreSnapshots
            .AsNoTracking()
            .Where(s => s.ScorecardId == scorecardId && s.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .Include(s => s.FiscalPeriod)
            .OrderBy(s => s.FiscalPeriod!.FiscalMonth)
            .ThenBy(s => s.Granularity)
            .ThenByDescending(s => s.CalculatedAt)
            .ToListAsync();
    }
}
