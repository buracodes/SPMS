using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Jobs
{
    public static class JobStartupExtensions
    {
        public static IServiceProvider RegisterRecurringJobs(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var registrar = scope.ServiceProvider.GetRequiredService<ISpmsRecurringJobRegistrar>();
            registrar.RegisterAll();
            return services;
        }
    }
}
