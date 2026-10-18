using Domain.Entities;

namespace Application.Interface.IService;

/// <summary>
/// Resolves a fiscal period's lock state. Day-level decisions (entry window, cutoff) are
/// evaluated in ECX business time, so when no reference date is supplied the injected
/// <see cref="IBusinessClock"/>'s <see cref="IBusinessClock.BusinessDate"/> is used.
/// </summary>
public interface IPeriodLockStatusService
{
    Task<Application.Services.PeriodLockInfo> GetStatusAsync(Guid scorecardId, Guid fiscalPeriodId, DateTime? asOfBusinessDate = null);

    Task<Application.Services.PeriodLockInfo> GetStatusAsync(Guid scorecardId, FiscalPeriod period, DateTime? asOfBusinessDate = null);

    Task<IReadOnlyList<Application.Services.PeriodLockInfo>> GetStatusesAsync(
        Guid scorecardId,
        IReadOnlyList<FiscalPeriod> periods,
        IReadOnlyList<Domain.Entities.PeriodLockConfig> explicitConfigs,
        DateTime? asOfBusinessDate = null);
}
