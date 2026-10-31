using Application.Interface.IRepository;
using Application.Tests.TestSupport;
using Domain.Entities;
using Infrastructure.Jobs;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Jobs;

public class ExpiredRefreshTokenCleanupJobTests
{
    [Fact]
    public async Task ExecuteAsync_RemovesExpiredAndRevokedRefreshTokens()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "token-user",
            FullName = "Token User",
        };

        lease.Context.Users.Add(user);
        lease.Context.RefreshTokens.AddRange(
            NewRefreshToken(user.Id, "expired", now.AddMinutes(-1)),
            NewRefreshToken(user.Id, "revoked", now.AddHours(1), revokedAtUtc: now),
            NewRefreshToken(user.Id, "active", now.AddHours(1)));
        await lease.Context.SaveChangesAsync();

        var services = new ServiceCollection()
            .AddDbContext<SpmsDbContext>(options => options.UseSqlite(lease.Connection))
            .AddScoped<IRefreshTokenRepository, RefreshTokenRepository>()
            .BuildServiceProvider();
        await using var serviceProvider = services;

        var job = new ExpiredRefreshTokenCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExpiredRefreshTokenCleanupJob>.Instance);

        await job.ExecuteAsync();

        var remainingTokens = await lease.Context.RefreshTokens.AsNoTracking().ToListAsync();

        var remainingToken = Assert.Single(remainingTokens);
        Assert.Equal("active", remainingToken.TokenHash);
    }

    private static RefreshToken NewRefreshToken(
        Guid userId,
        string tokenHash,
        DateTime expiresAtUtc,
        DateTime? revokedAtUtc = null)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = revokedAtUtc
        };
    }
}
