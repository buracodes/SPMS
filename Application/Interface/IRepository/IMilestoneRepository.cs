using Domain.Enums;
using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IMilestoneRepository : IGenericRepository<Milestone>
{
    Task<IReadOnlyList<Milestone>> GetByInitiativeIdAsync(Guid initiativeId);

    Task<Milestone?> GetByIdForInitiativeAsync(Guid milestoneId, Guid initiativeId);

    Task<bool> ExistsByNameAsync(Guid initiativeId, string name, Guid? excludeId = null);

    /// <summary>Sum of the recorded weights for an initiative, used for the FR-402 "totals 100%" rule.</summary>
    Task<decimal> GetTotalWeightAsync(Guid initiativeId, Guid? excludeMilestoneId = null);
}
