using Application.Interface.IService;
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
        public static async Task InitializeDatabaseAsync(this IServiceProvider services, bool seedTemporaryData = false)
        {
            using var scope = services.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<SpmsDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

            await context.Database.MigrateAsync();

            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var defaultAdmin = scope.ServiceProvider.GetRequiredService<DefaultAdminOptions>();

            await SpmsDbSeeder.SeedAsync(context, passwordHasher, defaultAdmin, seedTemporaryData);

            logger.LogInformation("Database initialized and seeded successfully.");
        }
    }
}