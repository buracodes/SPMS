using Domain.Entities;
using Domain.Enums;

namespace Application.Interface.IRepository;

public interface IKpiScoreSnapshotRepository : IGenericRepository<KpiScoreSnapshot>
{
    Task<KpiScoreSnapshot?> GetLatestAsync(Guid kpiId, Guid fiscalPeriodId, SnapshotGranularity granularity);

    Task<IReadOnlyList<KpiScoreSnapshot>> GetForScorecardFiscalYearAsync(Guid scorecardId, Guid fiscalYearId);
}
