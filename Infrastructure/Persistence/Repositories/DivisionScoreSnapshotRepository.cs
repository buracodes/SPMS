using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DivisionScoreSnapshotRepository : GenericRepository<DivisionScoreSnapshot>, IDivisionScoreSnapshotRepository
{
    public DivisionScoreSnapshotRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<DivisionScoreSnapshot?> GetLatestAsync(Guid divisionGoalId, Guid fiscalPeriodId, SnapshotGranularity granularity)
    {
        return await context.DivisionScoreSnapshots
            .AsNoTracking()
            .Where(s => s.DivisionGoalId == divisionGoalId && s.FiscalPeriodId == fiscalPeriodId && s.Granularity == granularity)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<DivisionScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid divisionId, Guid fiscalYearId)
    {
        return await context.DivisionScoreSnapshots
            .AsNoTracking()
            .Where(s => s.ScorecardId == scorecardId && s.DivisionId == divisionId && s.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .Include(s => s.FiscalPeriod)
            .OrderBy(s => s.FiscalPeriod!.FiscalMonth)
            .ThenBy(s => s.Granularity)
            .ThenByDescending(s => s.CalculatedAt)
            .ToListAsync();
    }
}
