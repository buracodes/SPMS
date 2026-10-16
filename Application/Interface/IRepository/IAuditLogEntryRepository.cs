using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IAuditLogEntryRepository
{
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int take);
}