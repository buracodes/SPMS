using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiActualValueRepository : IGenericRepository<KpiActualValue>
{
    /// <summary>
    /// Tracked lookup of the one current revision for a KPI + fiscal period. Used by the
    /// append-only correction path to supersede the previous value inside a transaction.
    /// </summary>
    Task<KpiActualValue?> GetCurrentAsync(
        Guid kpiId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);

    /// <summary>Current revision only, for calculations and current-value reads.</summary>
    Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId);

    Task<IReadOnlyList<KpiActualValue>> GetForFiscalYearAsync(Guid kpiId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiActualValue>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);

    /// <summary>
    /// All revisions for a KPI + fiscal period, including superseded ones. Intended for
    /// authorized history access; current-value paths must not use this method.
    /// </summary>
    Task<IReadOnlyList<KpiActualValue>> GetByKpiAndPeriodAsync(Guid kpiId, Guid fiscalPeriodId);

    Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default);
}
