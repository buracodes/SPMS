using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

/// <summary>
/// Dependencies are not a <see cref="Domain.Common.CommonEntity"/> (they are structural edges,
/// not auditable records in their own right), so this repository is hand written rather than
/// deriving from <see cref="GenericRepository{T}"/>.
/// </summary>
public class InitiativeDependencyRepository : IInitiativeDependencyRepository
{
    private readonly SpmsDbContext context;

    public InitiativeDependencyRepository(SpmsDbContext context)
    {
        this.context = context;
    }

    public Task<bool> ExistsAsync(Guid initiativeId, Guid dependsOnInitiativeId)
    {
        return context.InitiativeDependencies
            .AnyAsync(d => d.InitiativeId == initiativeId && d.DependsOnInitiativeId == dependsOnInitiativeId);
    }

    public Task<InitiativeDependency?> GetAsync(Guid initiativeId, Guid dependsOnInitiativeId)
    {
        return context.InitiativeDependencies
            .FirstOrDefaultAsync(d => d.InitiativeId == initiativeId && d.DependsOnInitiativeId == dependsOnInitiativeId);
    }

    public async Task AddAsync(InitiativeDependency dependency)
    {
        await context.InitiativeDependencies.AddAsync(dependency);
        await context.SaveChangesAsync();
    }

    public void Remove(InitiativeDependency dependency)
    {
        context.InitiativeDependencies.Remove(dependency);
    }

    public async Task<IReadOnlyList<(Guid InitiativeId, Guid DependsOnInitiativeId)>> GetAllEdgesAsync()
    {
        var rows = await context.InitiativeDependencies
            .AsNoTracking()
            .Select(d => new { d.InitiativeId, d.DependsOnInitiativeId })
            .ToListAsync();

        return rows.Select(r => (r.InitiativeId, r.DependsOnInitiativeId)).ToList();
    }
}
