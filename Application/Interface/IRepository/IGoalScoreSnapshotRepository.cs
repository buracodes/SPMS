using Domain.Entities;
using Domain.Enums;

namespace Application.Interface.IRepository;

public interface IGoalScoreSnapshotRepository : IGenericRepository<GoalScoreSnapshot>
{
    Task<GoalScoreSnapshot?> GetLatestAsync(Guid goalId, Guid fiscalPeriodId, SnapshotGranularity granularity);

    Task<IReadOnlyList<GoalScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);
}
