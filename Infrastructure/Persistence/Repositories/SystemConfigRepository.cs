using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SystemConfigRepository : GenericRepository<SystemConfig>, ISystemConfigRepository
{
    public SystemConfigRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<SystemConfig?> GetByKeyAsync(string key)
    {
        return await context.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);
    }

    public async Task<IReadOnlyList<SystemConfig>> GetManyAsync(IEnumerable<string> keys)
    {
        var keyList = keys.ToList();
        return await context.SystemConfigs
            .AsNoTracking()
            .Where(s => keyList.Contains(s.Key))
            .ToListAsync();
    }

    public async Task SetAsync(SystemConfig config)
    {
        var existing = await context.SystemConfigs
            .FirstOrDefaultAsync(s => s.Key == config.Key);

        if (existing == null)
        {
            config.Id = Guid.NewGuid();
            config.CreatedDate = DateTime.UtcNow;
            await context.SystemConfigs.AddAsync(config);
        }
        else
        {
            existing.ValueJson = config.ValueJson;
            existing.Category = config.Category;
            existing.UpdatedByUserId = config.UpdatedByUserId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
    }
}
