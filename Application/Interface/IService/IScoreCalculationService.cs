namespace Application.Interface.IService;

public interface IScoreCalculationService
{
    Task RecalculateKpiAsync(Guid kpiId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);

    Task RecalculateKpiCascadeAsync(Guid kpiId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);

    Task RecalculateUpwardCascadeAsync(Guid kraId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);

    Task RecalculateScorecardAsync(Guid scorecardId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);
}
