using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IFiscalPeriodRepository : IGenericRepository<FiscalPeriod>
{
    Task<IReadOnlyList<FiscalPeriod>> GetByFiscalYearAsync(Guid fiscalYearId);
}
