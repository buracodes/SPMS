using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IDivisionContributionRepository : IGenericRepository<DivisionContribution>
{
    Task<IReadOnlyList<Guid>> GetGoalIdsByPlanAsync(Guid divisionPlanId);
    Task<IReadOnlyList<DivisionContribution>> GetByPlanAsync(Guid divisionPlanId);
    Task<bool> ExistsInPlanAsync(Guid divisionPlanId, Guid goalId);
}
