using Application.Interface.IRepository;
using Domain.Entities;
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
}