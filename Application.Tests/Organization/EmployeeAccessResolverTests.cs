using Application.DTOS.AccessControl;
using Application.Tests.TestSupport;
using Domain.Entities;
using Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Organization;

public class EmployeeAccessResolverTests
{
    private static async Task<Division> AddDivisionAsync(
        TestDbContextLease lease, string name, bool isActive = true)
    {
        var division = new Division
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = name[..Math.Min(3, name.Length)].ToUpperInvariant(),
            IsActive = isActive
        };
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();
        return division;
    }

    private static async Task AddUserAsync(TestDbContextLease lease, Guid userId)
    {
        lease.Context.Users.Add(new User
        {
            Id = userId,
            Username = $"user-{userId:N}",
            FullName = "Scope Tester",
            IsActive = true
        });
        await lease.Context.SaveChangesAsync();
    }

    private static HrEmployeeAccessResolver CreateResolver(
        TestDbContextLease lease, Guid userId, Guid primaryDivisionId)
        => new(
            new TestHrInfoRepository(new[]
            {
                new EmployeeHRInfoDto { EmpId = userId, DivisionId = primaryDivisionId }
            }),
            lease.Context,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<HrEmployeeAccessResolver>.Instance);

    [Fact]
    public async Task ExplicitScope_IsReadLive_AndSoftDeleteRevokesItOnTheNextRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        await AddUserAsync(lease, userId);
        var primary = await AddDivisionAsync(lease, "Primary");
        var extra = await AddDivisionAsync(lease, "Extra");
        lease.Context.UserDivisionScopes.Add(new UserDivisionScope
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DivisionId = extra.Id
        });
        await lease.Context.SaveChangesAsync();

        var resolver = CreateResolver(lease, userId, primary.Id);

        var granted = await resolver.ResolveAsync(userId);
        Assert.Equal(primary.Id, granted.DivisionId);
        Assert.Equal(new[] { primary.Id, extra.Id }, granted.ScopedDivisionIds);

        var scope = await lease.Context.UserDivisionScopes.SingleAsync();
        scope.IsDeleted = true;
        await lease.Context.SaveChangesAsync();

        var revoked = await resolver.ResolveAsync(userId);
        Assert.Equal(new[] { primary.Id }, revoked.ScopedDivisionIds);
    }

    [Fact]
    public async Task ExplicitScope_InactiveDivision_IsNotGranted()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        await AddUserAsync(lease, userId);
        var primary = await AddDivisionAsync(lease, "Primary");
        var inactive = await AddDivisionAsync(lease, "Dormant", isActive: false);
        lease.Context.UserDivisionScopes.Add(new UserDivisionScope
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DivisionId = inactive.Id
        });
        await lease.Context.SaveChangesAsync();

        var snapshot = await CreateResolver(lease, userId, primary.Id).ResolveAsync(userId);

        Assert.Equal(new[] { primary.Id }, snapshot.ScopedDivisionIds);
    }

    [Fact]
    public async Task NoExplicitScopes_KeepsOnlyTheHrDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        await AddUserAsync(lease, userId);
        var primary = await AddDivisionAsync(lease, "Primary");

        var snapshot = await CreateResolver(lease, userId, primary.Id).ResolveAsync(userId);

        Assert.True(snapshot.IsActiveHrEmployee);
        Assert.Equal(primary.Id, snapshot.DivisionId);
        Assert.Equal(new[] { primary.Id }, snapshot.ScopedDivisionIds);
    }

    [Fact]
    public async Task Snapshot_DeduplicatesAnExplicitGrantForThePrimaryDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        await AddUserAsync(lease, userId);
        var primary = await AddDivisionAsync(lease, "Primary");
        lease.Context.UserDivisionScopes.Add(new UserDivisionScope
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DivisionId = primary.Id
        });
        await lease.Context.SaveChangesAsync();

        var snapshot = await CreateResolver(lease, userId, primary.Id).ResolveAsync(userId);

        Assert.Equal(new[] { primary.Id }, snapshot.ScopedDivisionIds);
    }
}
