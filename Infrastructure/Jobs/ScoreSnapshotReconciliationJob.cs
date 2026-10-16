using System.Threading.Tasks;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Jobs
{
    public sealed class ScoreSnapshotReconciliationJob : ISpmsJob
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScoreSnapshotReconciliationJob> _logger;

        public ScoreSnapshotReconciliationJob(
            IServiceScopeFactory scopeFactory,
            ILogger<ScoreSnapshotReconciliationJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var scorecardRepository = scope.ServiceProvider.GetRequiredService<IScorecardRepository>();
            var periodRepository = scope.ServiceProvider.GetRequiredService<IFiscalPeriodRepository>();
            var service = scope.ServiceProvider.GetRequiredService<IScoreCalculationService>();

            var scorecards = await scorecardRepository.GetAll();
            foreach (var scorecard in scorecards)
            {
                var periods = await periodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);
                foreach (var period in periods)
                {
                    await service.RecalculateScorecardAsync(scorecard.Id, period.Id);
                }
            }

            _logger.LogInformation("Score snapshot reconciliation completed for {Count} scorecard(s).", scorecards.Count);
        }
    }
}
