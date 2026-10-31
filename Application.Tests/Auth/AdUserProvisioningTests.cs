using Application.Interface.IService;
using Application.Tests.TestSupport;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Application.Tests.Auth;

/// <summary>
/// First-login provisioning. AD authenticates; SPMS only creates the local authorization row.
/// The account starts with no role (default deny), and a role is assigned only when the caller
/// explicitly provisions one — currently just the configured bootstrap administrators.
/// </summary>
public class AdUserProvisioningTests : IAsyncDisposable
{
    private readonly TestDbContextLease lease;

    public AdUserProvisioningTests()
    {
        lease = TestDbContextFactory.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task FirstLoginProvisionsTheAccountWithoutAnyRole()
    {
        await SeedRolesAndRightsAsync();
        var repository = new UserRepository(lease.Context);

        var user = await repository.ProvisionAdUserAsync(
            Guid.NewGuid(), "dereje.kebede", "Dereje Kebede", "dereje.kebede@ecx.com.et");

        Assert.NotNull(user);
        Assert.Empty(user!.UserRoles);
        Assert.Empty(await EffectiveGrantsAsync(user.Id));
    }

    [Fact]
    public async Task ExplicitBootstrapRoleIsAssigned()
    {
        await SeedRolesAndRightsAsync();
        var repository = new UserRepository(lease.Context);

        var user = await repository.ProvisionAdUserAsync(
            Guid.NewGuid(), "bootstrap.admin", "Bootstrap Admin", null, Roles.Admin);

        Assert.NotNull(user);
        var membership = Assert.Single(user!.UserRoles);
        Assert.Equal(Roles.Admin, membership.Role.Name);
    }

    [Fact]
    public async Task UnknownRoleNameFailsInsteadOfSilentlyProvisioningNothing()
    {
        await SeedRolesAndRightsAsync();
        var repository = new UserRepository(lease.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ProvisionAdUserAsync(Guid.NewGuid(), "typo.admin", "Typo Admin", null, "Admn"));
    }

    private async Task SeedRolesAndRightsAsync()
    {
        var context = lease.Context;

        foreach (var name in Roles.All)
        {
            context.Roles.Add(new Role { Id = Guid.NewGuid(), Name = name });
        }

        foreach (var permissionName in new[]
        {
            Permissions.DashboardView, Permissions.KpiView, Permissions.AdminRolesManage
        })
        {
            context.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(),
                Name = permissionName,
                Module = permissionName.Split(':')[0]
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private async Task<IReadOnlySet<string>> EffectiveGrantsAsync(Guid userId)
    {
        var service = new EffectivePermissionService(lease.Context, new NoPositionGrants());
        return await service.GetEffectiveGrantNamesAsync(userId);
    }

    private sealed class NoPositionGrants : IPositionPermissionProvider
    {
        public Task<IReadOnlySet<string>> GetGrantedPermissionsAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    public ValueTask DisposeAsync() => lease.DisposeAsync();
}
