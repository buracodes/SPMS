using Hangfire;
using Hangfire.SqlServer;

namespace Infrastructure.Jobs
{
    public static class HangfireStorageConfiguration
    {
        public static IGlobalConfiguration UseSpmsSqlServerStorage(this IGlobalConfiguration configuration, string connectionString)
        {
            return configuration.UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = "HangFire",
                PrepareSchemaIfNecessary = true,
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            });
        }
    }
}
