using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IFiscalYearRepository : IGenericRepository<FiscalYear>
{
    Task<IReadOnlyList<FiscalYear>> GetActiveAsync(CancellationToken cancellationToken = default);
}
