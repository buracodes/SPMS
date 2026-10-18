using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IGoalRepository : IGenericRepository<Goal>
{
    Task<Goal?> GetByIdWithKrasAsync(Guid id);
    Task<IReadOnlyList<Goal>> GetByScorecardAsync(Guid scorecardId);
    Task<IReadOnlyList<Guid>> GetGoalIdsByScorecardAsync(Guid scorecardId);

    /// <summary>
    /// Server-side SUM of active (non soft-deleted) goal weights within one scorecard.
    /// Evaluated in SQL so the weight-budget guard never loads the whole table into
    /// memory; combined with IUnitOfWork.ExecuteInTransactionAsync it closes the
    /// read-then-write race documented in ADR-009.
    /// </summary>
    Task<decimal> GetWeightsSumAsync(Guid scorecardId, Guid? excludeGoalId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the goal has any KRA row, including soft-deleted ones. Deletion
    /// integrity must see rows the global filter hides from normal reads, so a
    /// soft-deleted KRA still blocks goal deletion.
    /// </summary>
    Task<bool> HasAnyKrasIncludingDeletedAsync(Guid goalId, CancellationToken cancellationToken = default);
}