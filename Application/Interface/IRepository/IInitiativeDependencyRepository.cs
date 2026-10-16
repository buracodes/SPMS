using Domain.Entities;

namespace Application.Interface.IRepository;

/// <summary>
/// Initiative-to-initiative prerequisite edges. Cycle prevention lives in the application
/// layer; this interface only provides the graph reads needed to detect it.
/// </summary>
public interface IInitiativeDependencyRepository
{
    Task<bool> ExistsAsync(Guid initiativeId, Guid dependsOnInitiativeId);

    Task<InitiativeDependency?> GetAsync(Guid initiativeId, Guid dependsOnInitiativeId);

    Task AddAsync(InitiativeDependency dependency);

    void Remove(InitiativeDependency dependency);

    /// <summary>Every edge as (dependent, prerequisite) pairs. Used for cycle detection and detail views.</summary>
    Task<IReadOnlyList<(Guid InitiativeId, Guid DependsOnInitiativeId)>> GetAllEdgesAsync();
}
