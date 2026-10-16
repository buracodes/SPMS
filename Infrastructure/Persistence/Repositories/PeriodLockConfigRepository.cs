using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class PeriodLockConfigRepository : GenericRepository<PeriodLockConfig>, IPeriodLockConfigRepository
{
    public PeriodLockConfigRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<PeriodLockConfig?> GetByScorecardAndPeriodAsync(Guid scorecardId, Guid fiscalPeriodId)
    {
        return await context.PeriodLockConfigs
            .FirstOrDefaultAsync(c => c.ScorecardId == scorecardId && c.FiscalPeriodId == fiscalPeriodId);
    }

    public async Task<IReadOnlyList<PeriodLockConfig>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.PeriodLockConfigs
            .AsNoTracking()
            .Where(c => c.ScorecardId == scorecardId)
            .Join(
                context.FiscalPeriods.Where(fp => fp.FiscalYearId == fiscalYearId),
                c => c.FiscalPeriodId,
                fp => fp.Id,
                (c, fp) => c
            )
            .ToListAsync();
    }
}
