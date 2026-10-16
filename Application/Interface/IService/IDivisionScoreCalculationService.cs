namespace Application.Interface.IService;

public interface IDivisionScoreCalculationService
{
    Task RecalculateDivisionGoalAsync(Guid divisionGoalId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);

    Task RecalculateDivisionCascadeAsync(Guid divisionGoalId, Guid fiscalPeriodId, CancellationToken cancellationToken = default);
}
