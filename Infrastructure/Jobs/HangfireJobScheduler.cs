using Application.Jobs;
using Hangfire;
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Infrastructure.Jobs
{
    internal sealed class HangfireJobScheduler : ISpmsJobScheduler
    {
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IRecurringJobManager _recurringJobManager;

        public HangfireJobScheduler(
            IBackgroundJobClient backgroundJobClient,
            IRecurringJobManager recurringJobManager)
        {
            _backgroundJobClient = backgroundJobClient;
            _recurringJobManager = recurringJobManager;
        }

        public string Enqueue<TJob>() where TJob : ISpmsJob
            => _backgroundJobClient.Enqueue<TJob>(job => job.ExecuteAsync());

        public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall) where TJob : ISpmsJob
            => _backgroundJobClient.Enqueue(methodCall);

        public string ScheduleRecurring<TJob>(string jobId, string cronExpression) where TJob : ISpmsJob
        {
            _recurringJobManager.AddOrUpdate<TJob>(jobId, job => job.ExecuteAsync(), cronExpression);
            return jobId;
        }

        public void RemoveRecurring(string jobId)
            => _recurringJobManager.RemoveIfExists(jobId);
    }
}
