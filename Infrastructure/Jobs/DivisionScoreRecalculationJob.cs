using Application.Interface.IService;
using Application.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Jobs;

public sealed class DivisionScoreRecalculationJob : IDivisionScoreRecalculationJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DivisionScoreRecalculationJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RecalculateDivisionGoalAsync(Guid divisionGoalId, Guid fiscalPeriodId)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDivisionScoreCalculationService>();
        await service.RecalculateDivisionCascadeAsync(divisionGoalId, fiscalPeriodId);
    }

    public Task ExecuteAsync() => Task.CompletedTask;
}
