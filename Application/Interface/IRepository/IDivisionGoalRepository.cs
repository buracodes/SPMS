using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IDivisionGoalRepository : IGenericRepository<DivisionGoal>
{
    Task<DivisionGoal?> GetByIdWithPlanAsync(Guid id);
    Task<IReadOnlyList<DivisionGoal>> GetByPlanWithKpisAsync(Guid divisionPlanId);
    Task<IReadOnlyList<DivisionGoal>> GetByPlanIdsWithKpisAsync(IReadOnlyList<Guid> divisionPlanIds);
    Task<IReadOnlyList<DivisionGoal>> GetByPlanIdsAsync(IReadOnlyList<Guid> divisionPlanIds);
    Task<bool> ExistsByNameInPlanAsync(Guid divisionPlanId, string name, Guid? excludeId = null);
    Task<bool> ExistsUnderParentGoalsAsync(Guid divisionPlanId, IReadOnlyList<Guid> parentGoalIds);
    Task<decimal> GetWeightsSumAsync(Guid divisionPlanId, Guid? excludeGoalId = null);
}
