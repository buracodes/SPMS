using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiTargetRepository : IGenericRepository<KpiTarget>
{
    Task<IReadOnlyList<KpiTarget>> GetByKpiAndFiscalYearAsync(Guid kpiId, Guid fiscalYearId);

    Task<IReadOnlyList<KpiTarget>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);

    Task ReplaceForFiscalYearAsync(Guid kpiId, Guid fiscalYearId, IEnumerable<KpiTarget> targets);

    Task<bool> AnyForPeriodsAsync(IEnumerable<Guid> fiscalPeriodIds, CancellationToken cancellationToken = default);
}