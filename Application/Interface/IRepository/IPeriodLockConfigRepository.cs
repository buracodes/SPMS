using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IPeriodLockConfigRepository : IGenericRepository<PeriodLockConfig>
{
    Task<PeriodLockConfig?> GetByScorecardAndPeriodAsync(Guid scorecardId, Guid fiscalPeriodId);

    Task<IReadOnlyList<PeriodLockConfig>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);
}
