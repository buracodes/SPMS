using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IDivisionPlanRepository : IGenericRepository<DivisionPlan>
{
    Task<DivisionPlan?> GetByDivisionAndScorecardAsync(Guid divisionId, Guid scorecardId);
    Task<DivisionPlan?> GetByDivisionAndScorecardWithDetailsAsync(Guid divisionId, Guid scorecardId);
    Task<DivisionPlan?> GetFirstByScorecardWithDetailsAsync(Guid scorecardId);
    Task<IReadOnlyList<DivisionPlan>> GetByDivisionAsync(Guid divisionId);
    Task<IReadOnlyList<DivisionPlan>> GetByScorecardAsync(Guid scorecardId);
    Task<IReadOnlyList<DivisionPlan>> GetByDivisionIdsAsync(IReadOnlyList<Guid> divisionIds);
}
