using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Application.Jobs
{
    public interface ISpmsJobScheduler
    {
        string Enqueue<TJob>() where TJob : ISpmsJob;

        string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall) where TJob : ISpmsJob;

        string ScheduleRecurring<TJob>(string jobId, string cronExpression) where TJob : ISpmsJob;

        void RemoveRecurring(string jobId);
    }
}
