using Application.Jobs;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Application.Tests.TestSupport;

public class TestSpmsJobScheduler : ISpmsJobScheduler
{
    public List<string> EnqueuedJobIds { get; } = new();

    public string Enqueue<TJob>() where TJob : ISpmsJob
    {
        EnqueuedJobIds.Add(typeof(TJob).Name);
        return typeof(TJob).Name;
    }

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall) where TJob : ISpmsJob
    {
        EnqueuedJobIds.Add(typeof(TJob).Name);
        return typeof(TJob).Name;
    }

    public string ScheduleRecurring<TJob>(string jobId, string cronExpression) where TJob : ISpmsJob
        => jobId;

    public void RemoveRecurring(string jobId)
    {
    }
}
