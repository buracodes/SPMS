using Application.DTOS.PerformanceMonitoring;

namespace Application.Interface.IService;

public interface IPerformanceMonitoringService
{
    Task<PerformanceMonitoringDto> GetAsync(PerformanceMonitoringFilter filter, CancellationToken cancellationToken = default);
}
