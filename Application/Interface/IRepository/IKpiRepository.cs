using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiRepository : IGenericRepository<Kpi>
{
    Task<Kpi?> GetByIdWithScorecardAsync(Guid id);
    Task<IReadOnlyList<Kpi>> GetAllKpisWithDetailsAsync();
    Task<IReadOnlyList<Kpi>> GetByScorecardAsync(Guid scorecardId);
    Task<bool> ExistsUnderDivisionGoalAsync(Guid divisionGoalId, string name, Guid? excludeKpiId = null);
    Task<bool> ExistsUnderKraAsync(Guid kraId, string name, Guid? excludeKpiId = null);
    /// <summary>SQL-side SUM of weights across KPIs under a KRA (soft-deleted excluded by the global query filter), optionally excluding one KPI (edit scenarios).</summary>
    Task<decimal> GetKraKpiWeightsSumAsync(Guid kraId, Guid? excludeKpiId = null);
    Task<decimal> GetDivisionKpiWeightsSumAsync(Guid divisionGoalId, Guid? excludeKpiId = null);
    Task<IReadOnlyDictionary<Guid, int>> GetDivisionKpiCountsByGoalAsync(IReadOnlyList<Guid> divisionGoalIds);
    /// <summary>Loads a KPI plus its full descendant tree ignoring soft-delete filters, for safe hierarchical deletion guards.</summary>
    Task<IReadOnlyList<Kpi>> GetSubtreeIncludingInactiveAsync(Guid rootKpiId);
    /// <summary>True when any of the given KPIs has at least one actual value row ever recorded, including zero-valued rows (filter-unaware probe).</summary>
    Task<bool> HasAnyActualDataAsync(IReadOnlyList<Guid> kpiIds);
}