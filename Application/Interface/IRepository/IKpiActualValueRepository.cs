using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiActualValueRepository : IGenericRepository<KpiActualValue>
{
    Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId, Domain.Enums.KpiDataSource dataSource);

    Task<KpiActualValue?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId);

    Task<IReadOnlyList<KpiActualValue>> GetForFiscalYearAsync(Guid kpiId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiActualValue>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiActualValue>> GetByKpiAndPeriodAsync(Guid kpiId, Guid fiscalPeriodId);

    Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default);
}