using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DivisionRepository : GenericRepository<Division>, IDivisionRepository
{
    public DivisionRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null)
    {
        return await context.Divisions.AnyAsync(d => d.Name == name && d.Id != excludeId);
    }

    public async Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null)
    {
        return await context.Divisions.AnyAsync(d => d.Code == code && d.Id != excludeId);
    }

    public async Task<IReadOnlyList<Division>> GetActiveAsync()
    {
        return await context.Divisions
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }
}
