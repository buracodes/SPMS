using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKpiRepository : IGenericRepository<Kpi>
{
    Task<Kpi?> GetByIdWithScorecardAsync(Guid id);
    Task<IReadOnlyList<Kpi>> GetAllKpisWithDetailsAsync();
    Task<IReadOnlyList<Kpi>> GetByScorecardAsync(Guid scorecardId);
    Task<bool> ExistsUnderDivisionGoalAsync(Guid divisionGoalId, string name);
    Task<decimal> GetDivisionKpiWeightsSumAsync(Guid divisionGoalId, Guid? excludeKpiId = null);
    Task<IReadOnlyDictionary<Guid, int>> GetDivisionKpiCountsByGoalAsync(IReadOnlyList<Guid> divisionGoalIds);
}