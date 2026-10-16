using Domain.Entities;
using Domain.Enums;

namespace Application.Interface.IRepository;

public interface IDivisionScoreSnapshotRepository : IGenericRepository<DivisionScoreSnapshot>
{
    Task<DivisionScoreSnapshot?> GetLatestAsync(Guid divisionGoalId, Guid fiscalPeriodId, SnapshotGranularity granularity);

    Task<IReadOnlyList<DivisionScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid divisionId, Guid fiscalYearId);
}
