using Domain.Entities;

namespace Application.Interface.IService;

public interface IPeriodLockStatusService
{
    Task<Application.Services.PeriodLockInfo> GetStatusAsync(Guid scorecardId, Guid fiscalPeriodId, DateTime? nowUtc = null);

    Task<Application.Services.PeriodLockInfo> GetStatusAsync(Guid scorecardId, FiscalPeriod period, DateTime? nowUtc = null);

    Task<IReadOnlyList<Application.Services.PeriodLockInfo>> GetStatusesAsync(
        Guid scorecardId,
        IReadOnlyList<FiscalPeriod> periods,
        IReadOnlyList<Domain.Entities.PeriodLockConfig> explicitConfigs,
        DateTime? nowUtc = null);
}
