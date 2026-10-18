using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiActualValueRepository : IGenericRepository<KpiActualValue>
{
    Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId, Domain.Enums.KpiDataSource dataSource);

    Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId);

    Task<IReadOnlyList<KpiActualValue>> GetForFiscalYearAsync(Guid kpiId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiActualValue>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiActualValue>> GetByKpiAndPeriodAsync(Guid kpiId, Guid fiscalPeriodId);

    /// <summary>
    /// FR-309 day-dedupe: deactivates (never deletes) every automated actual row of one
    /// KPI + fiscal period recorded on the same UTC day, so the latest same-day write
    /// survives. Staged only - the caller commits with SaveChanges together with the new
    /// write it is about to add.
    /// </summary>
    Task SoftDeleteAutomatedSameDayAsync(Guid kpiId, Guid fiscalPeriodId, DateTime utcDay);

    Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default);
}