using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KraRepository : GenericRepository<Kra>, IKraRepository
{
    public KraRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<Kra?> GetByIdWithKpisAsync(Guid id)
    {
        return await context.Kras
            .Include(k => k.Kpis)
            .FirstOrDefaultAsync(k => k.Id == id);
    }
}
