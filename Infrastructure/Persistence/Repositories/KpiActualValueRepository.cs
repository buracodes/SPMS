using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KpiActualValueRepository : GenericRepository<KpiActualValue>, IKpiActualValueRepository
{
    public KpiActualValueRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId, KpiDataSource dataSource)
    {
        return await context.KpiActualValues
            .AsNoTracking()
            .Where(a => a.KpiId == kpiId && a.FiscalPeriodId == fiscalPeriodId && a.DataSource == dataSource)
            .OrderByDescending(a => a.AsOfDate)
            .FirstOrDefaultAsync();
    }

    public async Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId)
    {
        return await context.KpiActualValues
            .AsNoTracking()
            .Where(a => a.KpiId == kpiId && a.FiscalPeriodId == fiscalPeriodId)
            .OrderByDescending(a => a.AsOfDate)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<KpiActualValue>> GetForFiscalYearAsync(Guid kpiId, Guid fiscalYearId)
    {
        return await context.KpiActualValues
            .Where(a => a.KpiId == kpiId && a.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .OrderBy(a => a.FiscalPeriod!.FiscalMonth)
            .ThenBy(a => a.DataSource)
            .ThenByDescending(a => a.AsOfDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<KpiActualValue>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.KpiActualValues
            .AsNoTracking()
            .Include(a => a.FiscalPeriod)
            .Where(a => a.Kpi!.ScorecardId == scorecardId && a.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .OrderBy(a => a.FiscalPeriod!.FiscalMonth)
            .ThenByDescending(a => a.AsOfDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<KpiActualValue>> GetByKpiAndPeriodAsync(Guid kpiId, Guid fiscalPeriodId)
    {
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