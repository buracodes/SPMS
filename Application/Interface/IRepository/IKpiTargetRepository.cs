using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiTargetRepository : IGenericRepository<KpiTarget>
{
    Task<IReadOnlyList<KpiTarget>> GetByKpiAndFiscalYearAsync(Guid kpiId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiTarget>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);

    /// <summary>
    /// Upserts targets for a KPI within a fiscal year, keyed by
    /// (KpiId, FiscalPeriodId, Granularity). Existing rows are edited in place, missing
    /// combinations are inserted and stale combinations are removed — but the changes are
    /// only staged. The caller must commit via
    /// <see cref="Application.Interface.IUnitOfWork.SaveChangesAsync"/> so the target rewrite
    /// and any other pending changes (for example newly generated fiscal periods) commit in a
    /// single transaction.
    /// </summary>
    Task ReplaceForFiscalYearAsync(Guid kpiId, Guid fiscalYearId, IEnumerable<KpiTarget> targets);

    Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default);
}