using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KpiActualValueRepository : GenericRepository<KpiActualValue>, IKpiActualValueRepository
{
    public KpiActualValueRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<KpiActualValue?> GetCurrentAsync(
        Guid kpiId, Guid fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        // Tracked on purpose: the caller marks this row non-current and links it to the
        // replacement in the same transaction.
        return await context.KpiActualValues
            .FirstOrDefaultAsync(
                a => a.KpiId == kpiId && a.FiscalPeriodId == fiscalPeriodId && a.IsCurrent,
                cancellationToken);
    }

    public async Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId)
    {
        return await context.KpiActualValues
            .AsNoTracking()
            .Where(a => a.KpiId == kpiId && a.FiscalPeriodId == fiscalPeriodId && a.IsCurrent)
            .OrderByDescending(a => a.AsOfDate)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<KpiActualValue>> GetForFiscalYearAsync(Guid kpiId, Guid fiscalYearId)
    {
        return await context.KpiActualValues
            .Where(a => a.KpiId == kpiId
                && a.IsCurrent
                && a.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .OrderBy(a => a.FiscalPeriod!.FiscalMonth)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<KpiActualValue>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.KpiActualValues
            .AsNoTracking()
            .Include(a => a.FiscalPeriod)
            .Where(a => a.IsCurrent
                && a.Kpi!.ScorecardId == scorecardId
                && a.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .OrderBy(a => a.FiscalPeriod!.FiscalMonth)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<KpiActualValue>> GetByKpiAndPeriodAsync(Guid kpiId, Guid fiscalPeriodId)
    {
        // History deliberately includes superseded revisions. IsDeleted/Status filtering is
        // left to the global query filter; supersession is represented by IsCurrent only.
        return await context.KpiActualValues
            .AsNoTracking()
            .Where(a => a.KpiId == kpiId && a.FiscalPeriodId == fiscalPeriodId)
            .OrderByDescending(a => a.AsOfDate)
            .ToListAsync();
    }

    public async Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default)
    {
        var ids = fiscalPeriodIds.ToList();
        if (ids.Count == 0)
        {
            return false;
        }

        return await context.KpiActualValues
            .AnyAsync(a => ids.Contains(a.FiscalPeriodId), cancellationToken);
    }
}
