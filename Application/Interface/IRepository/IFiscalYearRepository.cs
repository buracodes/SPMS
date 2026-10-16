using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IFiscalYearRepository : IGenericRepository<FiscalYear>
{
    Task<IReadOnlyList<FiscalYear>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>True when a non-deleted year already uses <paramref name="label"/>.</summary>
    Task<bool> ExistsWithLabelAsync(string label, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a non-deleted year's calendar intersects the inclusive range
    /// [<paramref name="startDate"/>, <paramref name="endDate"/>].
    /// </summary>
    Task<bool> AnyOverlappingAsync(DateTime startDate, DateTime endDate, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
