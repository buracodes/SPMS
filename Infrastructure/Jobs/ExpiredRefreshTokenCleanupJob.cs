using Application.Interface.IRepository;
using Application.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Jobs
{
    public sealed class ExpiredRefreshTokenCleanupJob : ISpmsJob
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExpiredRefreshTokenCleanupJob> _logger;

        public ExpiredRefreshTokenCleanupJob(
            IServiceScopeFactory scopeFactory,
            ILogger<ExpiredRefreshTokenCleanupJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

            var removed = await repository.DeleteExpiredAsync(DateTime.UtcNow);
            _logger.LogInformation("Expired refresh token cleanup removed {RemovedCount} token(s).", removed);
        }
    }
}
