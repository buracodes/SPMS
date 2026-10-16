using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IScorecardRepository : IGenericRepository<Scorecard>
{
    Task<Scorecard?> GetByIdWithGoalsAsync(Guid id);

    Task<Scorecard?> GetByIdWithFiscalYearAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameInFiscalYearAsync(string name, Guid fiscalYearId);

    Task<bool> AnyForFiscalYearAsync(Guid fiscalYearId, CancellationToken cancellationToken = default);
}
