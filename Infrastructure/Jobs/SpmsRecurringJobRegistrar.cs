using Application.Jobs;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Jobs
{
    public interface ISpmsRecurringJobRegistrar
    {
        void RegisterAll();
    }

    internal sealed class SpmsRecurringJobRegistrar : ISpmsRecurringJobRegistrar
    {
        private readonly ISpmsJobScheduler _scheduler;
        private readonly IConfiguration _configuration;

        public SpmsRecurringJobRegistrar(
            ISpmsJobScheduler scheduler,
            IConfiguration configuration)
        {
            _scheduler = scheduler;
            _configuration = configuration;
        }

        public void RegisterAll()
        {
            var cleanupCron = _configuration["BackgroundJobs:RefreshTokenCleanupCron"] ?? "0 30 3 * * *";
            _scheduler.ScheduleRecurring<ExpiredRefreshTokenCleanupJob>(RecurringJobIds.RefreshTokenCleanup, cleanupCron);

            var reconciliationCron = _configuration["BackgroundJobs:ScoreSnapshotReconciliationCron"] ?? "0 45 3 * * *";
            _scheduler.ScheduleRecurring<ScoreSnapshotReconciliationJob>(RecurringJobIds.ScoreSnapshotReconciliation, reconciliationCron);

            var closureCron = _configuration["BackgroundJobs:FiscalYearClosureCron"] ?? "0 15 3 * * *";
            _scheduler.ScheduleRecurring<FiscalYearClosureJob>(RecurringJobIds.FiscalYearClosure, closureCron);
        }
    }
}
