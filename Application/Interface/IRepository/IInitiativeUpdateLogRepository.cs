using Domain.Entities;

namespace Application.Interface.IRepository;

/// <summary>Append-only read/write access to the initiative change history (FR-403).</summary>
public interface IInitiativeUpdateLogRepository : IGenericRepository<InitiativeUpdateLog>
{
    Task<IReadOnlyList<InitiativeUpdateLog>> GetForInitiativeAsync(Guid initiativeId, Guid? milestoneId = null);

    Task<InitiativeUpdateLog?> GetLatestForMilestoneAsync(Guid milestoneId);
}
