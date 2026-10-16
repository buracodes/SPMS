using Domain.Entities;

namespace Application.Interface.IRepository;

public interface ISystemConfigRepository : IGenericRepository<SystemConfig>
{
    Task<SystemConfig?> GetByKeyAsync(string key);

    Task<IReadOnlyList<SystemConfig>> GetManyAsync(IEnumerable<string> keys);

    Task SetAsync(SystemConfig config);
}
