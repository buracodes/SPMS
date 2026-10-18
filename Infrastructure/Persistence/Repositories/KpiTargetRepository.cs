using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KpiTargetRepository : GenericRepository<KpiTarget>, IKpiTargetRepository
{
    public KpiTargetRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<KpiTarget>> GetByKpiAndFiscalYearAsync(Guid kpiId, Guid fiscalYearId)
    {
        return await context.KpiTargets
            .Include(t => t.FiscalPeriod)
            .Where(t => t.KpiId == kpiId && t.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<KpiTarget>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId)
    {
        return await context.KpiTargets
            .AsNoTracking()
            .Include(t => t.FiscalPeriod)
            .Where(t => t.Kpi!.ScorecardId == scorecardId && t.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .ToListAsync();
    }

    /// <summary>
    /// Stages targets for a KPI within a fiscal year as an upsert keyed by
    /// (KpiId, FiscalPeriodId, Granularity). Existing records are updated in place,
    /// new combinations are inserted, and stale combinations are removed. This
    /// guarantees the same record is edited rather than replaced and prevents
    /// duplicate target rows even when a target already exists.
    /// <para>
    /// The changes are staged only; this method deliberately does not call SaveChanges so
    /// the caller can commit the target rewrite together with any other pending changes in
    /// one transaction.
    /// </para>
    /// </summary>
    public async Task ReplaceForFiscalYearAsync(Guid kpiId, Guid fiscalYearId, IEnumerable<KpiTarget> targets)
    {
        var incoming = targets.ToList();

        var existing = await context.KpiTargets
            .Include(t => t.FiscalPeriod)
            .Where(t => t.KpiId == kpiId && t.FiscalPeriod!.FiscalYearId == fiscalYearId)
            .ToListAsync();

        var existingByKey = existing.ToDictionary(t => (t.FiscalPeriodId, t.Granularity));
        var incomingKeys = new HashSet<(Guid fiscalPeriodId, TargetGranularity granularity)>(
            incoming.Select(t => (t.FiscalPeriodId, t.Granularity))
        );

        foreach (var target in incoming)
        {
            if (existingByKey.TryGetValue((target.FiscalPeriodId, target.Granularity), out var current))
            {
                current.TargetValue = target.TargetValue;
                current.UpdatedDate = DateTime.UtcNow;
            }
            else
            {
                context.KpiTargets.Add(target);
            }
        }

        var stale = existing
            .Where(t => !incomingKeys.Contains((t.FiscalPeriodId, t.Granularity)))
            .ToList();
        if (stale.Count > 0)
        {
            // Shared delete convention: never delete a row physically - deactivate it
            // (IsDeleted plus the generic EntityStatus lifecycle flag) so the historical
            // target values survive for audit while every filtered read stops seeing
            // them (the FR-304/FR-902 lineage rules).
            foreach (var target in stale)
            {
                target.IsDeleted = true;
                target.UpdatedDate = DateTime.UtcNow;
            }
        }
    }

    public async Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default)
    {
        var ids = fiscalPeriodIds.ToList();
        if (ids.Count == 0)
        {
            return false;
        }

        return await context.KpiTargets
            .AnyAsync(t => ids.Contains(t.FiscalPeriodId), cancellationToken);
    }
}
