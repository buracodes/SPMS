using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IDivisionRepository : IGenericRepository<Division>
{
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);
    Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null);
    Task<IReadOnlyList<Division>> GetActiveAsync();
}
