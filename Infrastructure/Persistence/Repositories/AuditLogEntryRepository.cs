using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class AuditLogEntryRepository : IAuditLogEntryRepository
{
    private readonly SpmsDbContext _context;

    public AuditLogEntryRepository(SpmsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int take)
    {
        return await _context.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(e => e.PerformedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetCreateReasonsAsync(
        string entityType,
        IEnumerable<Guid> entityIds,
        CancellationToken cancellationToken = default)
    {
        var ids = entityIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // Only the two columns needed by history are projected; no audit record is returned
        // to the application layer. EntityId is stored as the plain Guid string written by
        // the audit interceptor, so match it exactly.
        var entityIdValues = ids.Select(id => id.ToString()).ToList();
        var rows = await _context.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.EntityType == entityType
                && e.Action == AuditAction.Create
                && entityIdValues.Contains(e.EntityId))
            .OrderBy(e => e.EntityId)
            .ThenByDescending(e => e.PerformedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.EntityId, e.Reason })
            .ToListAsync(cancellationToken);

        var reasons = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            if (!Guid.TryParse(row.EntityId, out var entityId)
                || string.IsNullOrWhiteSpace(row.Reason))
            {
                continue;
            }

            // An id may have several Create rows after data repair. Newest non-empty reason
            // wins, but only rows already filtered to this exact entity type and id qualify.
            if (!reasons.ContainsKey(entityId))
            {
                reasons[entityId] = row.Reason.Trim();
            }
        }

        return reasons;
    }
}