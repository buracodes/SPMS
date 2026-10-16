using System;
using System.Threading.Tasks;
using Application.Interface.IService;
using Application.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Jobs
{
    public sealed class ScoreSnapshotRecalculationJob : IScoreSnapshotRecalculationJob
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ScoreSnapshotRecalculationJob(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task RecalculateKpiCascadeAsync(Guid kpiId, Guid fiscalPeriodId)
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IScoreCalculationService>();
            await service.RecalculateKpiCascadeAsync(kpiId, fiscalPeriodId);
        }

        public Task ExecuteAsync() => Task.CompletedTask;
    }
}
