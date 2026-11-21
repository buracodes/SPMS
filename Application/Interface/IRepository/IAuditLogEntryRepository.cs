using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IAuditLogEntryRepository
{
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int take);

    /// <summary>
    /// Reason text only, keyed by entity id, from Create audit rows. Callers pass the entity
    /// type and the exact revision ids so unrelated or Update audit rows are never returned.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCreateReasonsAsync(
        string entityType,
        IEnumerable<Guid> entityIds,
        CancellationToken cancellationToken = default);
}