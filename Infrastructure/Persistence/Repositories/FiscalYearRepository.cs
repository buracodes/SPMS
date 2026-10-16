using Application.Interface.IRepository;
using DocumentFormat.OpenXml.Wordprocessing;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FiscalYearRepository : GenericRepository<FiscalYear>, IFiscalYearRepository
{
    public FiscalYearRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<FiscalYear>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.FiscalYears
            .Where(d => d.Status == EntityStatus.Active && !d.IsDeleted)
            .OrderBy(d => d.StartDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsWithLabelAsync(string label, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await context.FiscalYears
            .Where(d => d.Label == label && (excludeId == null || d.Id != excludeId))
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> AnyOverlappingAsync(DateTime startDate, DateTime endDate, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var start = startDate.Date;
        var end = endDate.Date;

        return await context.FiscalYears
            .Where(d => d.StartDate <= end && d.EndDate >= start && (excludeId == null || d.Id != excludeId))
            .AnyAsync(cancellationToken);
    }
}
