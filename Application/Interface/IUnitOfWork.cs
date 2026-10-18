namespace Application.Interface;

public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside one serializable database transaction so
    /// read-then-write weight-budget guards (goal sum &lt;/= 100%) cannot race: two
    /// concurrent writers serialize on the parent aggregate instead of both passing
    /// the check (ADR-009 hardened approach, shared across weight-budget modules).
    /// The result is the value returned by <paramref name="work"/>; if it throws,
    /// the transaction is rolled back and the exception propagates unchanged -
    /// BadRequestException keeps its HTTP 400 mapping.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default);

    Task AddRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class;

    Task RemoveRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
