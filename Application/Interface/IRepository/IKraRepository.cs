using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IKraRepository : IGenericRepository<Kra>
{
    Task<Kra?> GetByIdWithKpisAsync(Guid id);
}