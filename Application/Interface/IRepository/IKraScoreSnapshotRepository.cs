using Domain.Entities;
using Domain.Enums;

namespace Application.Interface.IRepository;

public interface IKraScoreSnapshotRepository : IGenericRepository<KraScoreSnapshot>
{
    Task<KraScoreSnapshot?> GetLatestAsync(Guid kraId, Guid fiscalPeriodId, SnapshotGranularity granularity);

    Task<IReadOnlyList<KraScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);
}
