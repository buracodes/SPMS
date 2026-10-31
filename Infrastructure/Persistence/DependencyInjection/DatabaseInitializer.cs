using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.DependencyInjection
{
    public static class DatabaseInitializer
    {
        public static async Task InitializeDatabaseAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<SpmsDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

            await context.Database.MigrateAsync();
            await SpmsDbSeeder.SeedAsync(context);

            logger.LogInformation("Database initialized and seeded successfully.");
        }
    }
}
