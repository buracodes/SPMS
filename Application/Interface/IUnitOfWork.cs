namespace Application.Interface;

public interface IUnitOfWork
{
    Task AddRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class;

    Task RemoveRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
