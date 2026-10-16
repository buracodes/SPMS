using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IGoalRepository : IGenericRepository<Goal>
{
    Task<Goal?> GetByIdWithKrasAsync(Guid id);
    Task<IReadOnlyList<Goal>> GetByScorecardAsync(Guid scorecardId);
    Task<IReadOnlyList<Guid>> GetGoalIdsByScorecardAsync(Guid scorecardId);
}