using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class UnitOfMeasureRepository : GenericRepository<UnitsOfMeasure>, IUnitOfMeasureRepository
{
    public UnitOfMeasureRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<UnitsOfMeasure>> GetActiveAsync()
    {
        return await context.UnitsOfMeasures
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.SortOrder)
            .ToListAsync();
    }
}
