using Application.Interface;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class SpmsUnitOfWork : IUnitOfWork
{
    private readonly SpmsDbContext _context;

    public SpmsUnitOfWork(SpmsDbContext context)
    {
        _context = context;
    }

    public async Task AddRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class
        => await _context.Set<TEntity>().AddRangeAsync(entities, cancellationToken);

    public Task RemoveRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class
    {
        _context.Set<TEntity>().RemoveRange(entities);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
