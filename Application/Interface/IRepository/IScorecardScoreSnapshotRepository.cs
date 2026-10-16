using Domain.Entities;
using Domain.Enums;

namespace Application.Interface.IRepository;

public interface IScorecardScoreSnapshotRepository : IGenericRepository<ScorecardScoreSnapshot>
{
    Task<ScorecardScoreSnapshot?> GetLatestAsync(Guid scorecardId, Guid fiscalPeriodId, SnapshotGranularity granularity);

    Task<IReadOnlyList<ScorecardScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);
}
