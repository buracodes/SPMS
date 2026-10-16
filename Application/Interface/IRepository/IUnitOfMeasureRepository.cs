using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IUnitOfMeasureRepository : IGenericRepository<UnitsOfMeasure>
{
    Task<IReadOnlyList<UnitsOfMeasure>> GetActiveAsync();
}
