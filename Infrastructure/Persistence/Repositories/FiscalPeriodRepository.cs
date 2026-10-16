using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FiscalPeriodRepository : GenericRepository<FiscalPeriod>, IFiscalPeriodRepository
{
    public FiscalPeriodRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<FiscalPeriod>> GetByFiscalYearAsync(Guid fiscalYearId)
    {
        return await context.FiscalPeriods
            .Where(p => p.FiscalYearId == fiscalYearId)
            .OrderBy(p => p.FiscalMonth)
            .ToListAsync();
    }
}
