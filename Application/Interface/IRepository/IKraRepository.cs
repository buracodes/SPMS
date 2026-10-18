using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKraRepository : IGenericRepository<Kra>
{
    Task<Kra?> GetByIdWithKpisAsync(Guid id);

    /// <summary>
    /// Server-side SUM of active (non soft-deleted) KRA weights within one goal.
    /// Evaluated in SQL so the weight-budget guard never loads the whole collection into
    /// memory; combined with IUnitOfWork.ExecuteInTransactionAsync it closes the
    /// read-then-write race documented in ADR-009 (same approach as the goal budget).
    /// </summary>
    Task<decimal> GetWeightsSumAsync(Guid goalId, Guid? excludeKraId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the KRA has any KPI row, including soft-deleted ones. The global
    /// query filter hides soft-deleted KPIs from normal reads, and the FRD
    /// delete-integrity rule must not be fooled by them.
    /// </summary>
    Task<bool> HasKpisIncludingDeletedAsync(Guid kraId, CancellationToken cancellationToken = default);
}
