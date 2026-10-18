using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IScorecardRepository : IGenericRepository<Scorecard>
{
    Task<Scorecard?> GetByIdWithGoalsAsync(Guid id);

    Task<Scorecard?> GetByIdWithFiscalYearAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameInFiscalYearAsync(string name, Guid fiscalYearId);

    Task<bool> AnyForFiscalYearAsync(Guid fiscalYearId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every fiscal-year id that already has at least one scorecard. Used to flag the
    /// fiscal-year list in one query instead of one round-trip per row.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetFiscalYearIdsWithScorecardsAsync(CancellationToken cancellationToken = default);
}
