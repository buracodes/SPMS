using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ScorecardRepository : GenericRepository<Scorecard>, IScorecardRepository
{
    public ScorecardRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<Scorecard?> GetByIdWithGoalsAsync(Guid id)
    {
        return await context.Scorecards
            .Include(s => s.Goals)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Scorecard?> GetByIdWithFiscalYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Scorecards
            .Include(s => s.FiscalYear)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByNameInFiscalYearAsync(string name, Guid fiscalYearId)
    {
        return await context.Scorecards
            .AnyAsync(s => s.FiscalYearId == fiscalYearId && s.Name == name);
    }

    public async Task<bool> AnyForFiscalYearAsync(Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        return await context.Scorecards
            .AnyAsync(s => s.FiscalYearId == fiscalYearId, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetFiscalYearIdsWithScorecardsAsync(CancellationToken cancellationToken = default)
    {
        var ids = await context.Scorecards
            .Select(s => s.FiscalYearId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
