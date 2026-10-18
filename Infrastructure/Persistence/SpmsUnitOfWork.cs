using Application.Interface;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Persistence;

public sealed class SpmsUnitOfWork : IUnitOfWork
{
    private readonly SpmsDbContext _context;

    public SpmsUnitOfWork(SpmsDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Serializable on purpose (ADR-009): the shared read (weights sum) and the write
    /// below must see exactly one winner per parent aggregate. SQL Server takes key-range
    /// shared locks under SERIALIZABLE, so a concurrent insert into the same scorecard blocks
    /// until commit. SQLite (tests) serializes on the single writer connection, so the
    /// semantics are preserved without special wiring.
    /// </summary>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        // Nested use (e.g. a handler called within another transaction) deliberately joins the
        // ambient transaction instead of creating a second one.
        if (_context.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            TResult result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
