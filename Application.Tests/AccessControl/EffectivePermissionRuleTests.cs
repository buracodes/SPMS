using Application.CQRS.AccessControl;
using Application.CQRS.AccessControl.Handlers.Commands;
using Application.CQRS.AccessControl.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Permissions = Domain.Constants.Permissions;

namespace Application.Tests.AccessControl;

/// <summary>
/// Dynamic RBAC rules (ADR-011): the centralized precedence algorithm (deny wins), the
/// administrator set calculation, and the three safeguard rules (self-modification,
/// protected system groups, last administrator) as they run through the live evaluation
/// and the write handlers — against the production SQLite test context (EnsureCreated).
/// </summary>
public class EffectivePermissionRuleTests : IAsyncDisposable
{
    private readonly SqliteLease lease;

    public EffectivePermissionRuleTests()
    {
        lease = SqliteLease.Create().GetAwaiter().GetResult();
    }

    // ── Precedence ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UserWithoutAnyAssignmentIsDeniedByDefault()
    {
        var harness = await RbacHarness.CreateAsync(lease);

        var grants = await harness.Effective.GetEffectiveGrantNamesAsync(
            await harness.GetUserIdAsync("olivia"));

        Assert.Empty(grants);
    }

    [Fact]
    public async Task DirectGrantIsEffective()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddUserRightAsync(olivia, Permissions.KpiView, PermissionEffect.Grant);

        var grants = await harness.Effective.GetEffectiveGrantNamesAsync(olivia);

        Assert.Contains(Permissions.KpiView, grants);
        Assert.DoesNotContain(Permissions.KpiEdit, grants);
    }

    [Fact]
    public async Task GroupGrantAppliesToMembers()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.KpiView, PermissionEffect.Grant);
        await harness.AddMembershipAsync(olivia, "KPI Readers");

        var detail = await harness.Effective.EvaluateDetailedAsync(olivia);
        var entry = detail.Rights.First(r => r.PermissionName == Permissions.KpiView);

        Assert.Equal("Granted", entry.Result);
        Assert.Equal("Group Assignment", entry.Source);
        Assert.False(entry.HasExplicitDeny);
    }

    [Fact]
    public async Task DirectDenyOverridesGroupGrant()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.KpiView, PermissionEffect.Grant);
        await harness.AddMembershipAsync(olivia, "KPI Readers");
        await harness.AddUserRightAsync(olivia, Permissions.KpiView, PermissionEffect.Deny);

        var grants = await harness.Effective.GetEffectiveGrantNamesAsync(olivia);
        var detail = await harness.Effective.EvaluateDetailedAsync(olivia);
        var entry = detail.Rights.First(r => r.PermissionName == Permissions.KpiView);

        Assert.DoesNotContain(Permissions.KpiView, grants);
        Assert.Equal("Denied", entry.Result);
        Assert.Equal("Direct User Assignment", entry.Source);
        Assert.True(entry.HasExplicitDeny);
        Assert.Contains("overrides", entry.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupDenyOverridesDirectGrant()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.KpiView, PermissionEffect.Deny);
        await harness.AddMembershipAsync(olivia, "KPI Readers");
        await harness.AddUserRightAsync(olivia, Permissions.KpiView, PermissionEffect.Grant);

        var grants = await harness.Effective.GetEffectiveGrantNamesAsync(olivia);
        var detail = await harness.Effective.EvaluateDetailedAsync(olivia);
        var entry = detail.Rights.First(r => r.PermissionName == Permissions.KpiView);

        Assert.DoesNotContain(Permissions.KpiView, grants);
        Assert.Equal("Denied", entry.Result);
        Assert.Equal("Group Assignment", entry.Source);
        Assert.True(entry.HasExplicitDeny);
    }

    [Fact]
    public async Task DeactivatedIdentityIsDeniedEveryRight()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.KpiView, PermissionEffect.Grant);
        await harness.AddMembershipAsync(olivia, "KPI Readers");
        await harness.DeactivateUserAsync(olivia);

        var grants = await harness.Effective.GetEffectiveGrantNamesAsync(olivia);

        Assert.Empty(grants);
    }

    [Fact]
    public async Task SoftDeletedMembershipOrInactivatedRoleContributesNothing()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        var membershipId = await harness.AddMembershipAsync(olivia, "KPI Readers");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.KpiView, PermissionEffect.Grant);
        await harness.SoftDeleteMembershipAsync(membershipId);

        var whileMembershipDeleted =
            await harness.Effective.GetEffectiveGrantNamesAsync(olivia);
        Assert.Empty(whileMembershipDeleted);

        await harness.RestoreMembershipAsync(membershipId);
        await harness.DeactivateRoleAsync("KPI Readers");
        var afterRoleDeactivated =
            await harness.Effective.GetEffectiveGrantNamesAsync(olivia);
        Assert.Empty(afterRoleDeactivated);
    }

    // ── Administrator set ─────────────────────────────────────────────────

    [Fact]
    public async Task AdministratorRequiresBothManagementKeys()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.AdminUsersManage, PermissionEffect.Grant);
        await harness.AddMembershipAsync(olivia, "KPI Readers");

        Assert.Equal(0, await harness.Rbac.CountActiveAdministratorsAsync());
    }

    [Fact]
    public async Task DirectDenyRemovesUserFromAdministratorSet()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.AdminUsersManage, PermissionEffect.Grant);
        await harness.AddGroupRightAsync("KPI Readers", Permissions.AdminRolesManage, PermissionEffect.Grant);
        await harness.AddMembershipAsync(olivia, "KPI Readers");
        await harness.AddUserRightAsync(olivia, Permissions.AdminRolesManage, PermissionEffect.Deny);

        Assert.Equal(0, await harness.Rbac.CountActiveAdministratorsAsync());
    }

    [Fact]
    public async Task AdministratorCountCombinesGroupAndDirectGrants()
    {
        var harness = await RbacHarness.CreateAsync(lease);
        var olivia = await harness.GetUserIdAsync("olivia");
        await harness.AddGroupRightAsync("KPI Readers", Permissions.AdminUsersManage, PermissionEffect.Grant);
        await harness.AddMembershipAsync(olivia, "KPI Readers");
        await harness.AddUserRightAsync(olivia, Permissions.AdminRolesManage, PermissionEffect.Grant);

        var administratorIds = await harness.Rbac.GetActiveAdministratorUserIdsAsync();

        Assert.Equal(1, await harness.Rbac.CountActiveAdministratorsAsync());
        Assert.Contains(olivia, administratorIds);
        Assert.Equal(0, await harness.Rbac.CountActiveAdministratorsExcludingRoleAsync(
            await harness.GetRoleIdAsync("KPI Readers")));
    }

    // ── Safeguards through the write handlers ─────────────────────────────

    [Fact]
    public async Task AdministratorCannotAssignMembershipToThemselves()
    {
        var harness = await RbacHarness.CreateDefaultAdminScenario(lease);
        var adminRoleId = await harness.GetRoleIdAsync(Roles.All.First());

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.Handlers.Handle(
            new AssignGroupMembershipCommandRequest
            {
                GroupId = adminRoleId,
                UserId = harness.AdministratorService.UserId!.Value,
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task AdministratorCannotRemoveOwnMembership()
    {
        var harness = await RbacHarness.CreateDefaultAdminScenario(lease);
        var adminRoleId = await harness.GetRoleIdAsync(Roles.All.First());

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.Handlers.Handle(
            new RemoveGroupMembershipCommandRequest
            {
                GroupId = adminRoleId,
                UserId = harness.AdministratorService.UserId!.Value,
                Reason = "cleanup",
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task ProtectedSystemGroupCannotBeDeactivated()
    {
        var harness = await RbacHarness.CreateDefaultAdminScenario(lease);
        var adminRoleId = await harness.GetRoleIdAsync(Roles.All.First());

        await Assert.ThrowsAsync<BadRequestException>(() => harness.Handlers.Handle(
            new SetAccessGroupStatusCommandRequest
            {
                Id = adminRoleId,
                Dto = new AccessGroupStatusDto { IsActive = false },
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task RightsOfAGroupTheActorBelongsToCannotBeChanged()
    {
        var harness = await RbacHarness.CreateDefaultAdminScenario(lease);
        var actor = harness.AdministratorService.UserId!.Value;
        await harness.AddMembershipAsync(actor, "KPI Readers");
        var readersRoleId = await harness.GetRoleIdAsync("KPI Readers");
        var kpiViewId = await harness.GetPermissionIdAsync(Permissions.KpiView);

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.Handlers.Handle(
            new SetGroupRightCommandRequest
            {
                GroupId = readersRoleId,
                RightId = kpiViewId,
                Effect = PermissionEffect.Deny,
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task UserRightsCannotBeSelfGranted()
    {
        var harness = await RbacHarness.CreateDefaultAdminScenario(lease);
        var kpiViewId = await harness.GetPermissionIdAsync(Permissions.KpiView);

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.Handlers.Handle(
            new SetUserRightCommandRequest
            {
                UserId = harness.AdministratorService.UserId!.Value,
                RightId = kpiViewId,
                Effect = PermissionEffect.Grant,
                Reason = "self grant",
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task LastAdministratorsOnlyMembershipChangeIsBlocked()
    {
        var harness = await RbacHarness.CreateDefaultAdminScenario(lease);
        var coordinator = await harness.GetUserIdAsync("coordinator");
        var powerRoleId = await harness.GetRoleIdAsync("Power Users");

        // The coordinator is the only active holder of both management keys, while the
        // acting administrator is not part of the administrator set themselves.
        await harness.AddGroupRightAsync("Power Users", Permissions.AdminUsersManage, PermissionEffect.Grant);
        await harness.AddGroupRightAsync("Power Users", Permissions.AdminRolesManage, PermissionEffect.Grant);
        await harness.AddMembershipAsync(coordinator, "Power Users");
        await harness.RemoveMembershipAsync("administrator", Roles.All.First());

        await Assert.ThrowsAsync<BadRequestException>(() => harness.Handlers.Handle(
            new RemoveGroupMembershipCommandRequest
            {
                GroupId = powerRoleId,
                UserId = coordinator,
                Reason = "cleanup",
            },
            CancellationToken.None));
    }

    // ── Harness ───────────────────────────────────────────────────────────

    private sealed class SqliteLease : IAsyncDisposable
    {
        private readonly TestDbContextLease inner;

        private SqliteLease(TestDbContextLease inner)
        {
            this.inner = inner;
        }

        public static async Task<SqliteLease> Create() =>
            new(await TestDbContextFactory.CreateAsync());

        public SpmsDbContext Context => inner.Context;

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>
    /// Seed helper: one in-memory context per test with two users, four roles and the
    /// rights the precedence rules exercise, plus the acting-administrator service.
    /// </summary>
    private sealed class RbacHarness
    {
        public SpmsDbContext Context { get; }

        public RbacRepository Rbac { get; }

        public EffectivePermissionService Effective { get; }

        public TestCurrentUserService AdministratorService { get; private set; } = null!;

        public AccessControlCommandHandlers Handlers { get; private set; } = null!;

        private readonly SqliteLease lease;

        private RbacHarness(SqliteLease lease)
        {
            this.lease = lease;
            Context = lease.Context;
            Rbac = new RbacRepository(Context);
            Effective = new EffectivePermissionService(Context);
        }

        public static async Task<RbacHarness> CreateAsync(SqliteLease lease)
        {
            var harness = new RbacHarness(lease);
            await harness.SeedAsync();
            return harness;
        }

        public static async Task<RbacHarness> CreateDefaultAdminScenario(SqliteLease lease)
        {
            var harness = await CreateAsync(lease);
            var administrator = await harness.GetUserIdAsync("administrator");
            var adminRoleName = Roles.All.First();
            var adminRoleId = await harness.GetRoleIdAsync(adminRoleName);

            await harness.AddGroupRightAsync(adminRoleName, Permissions.AdminUsersManage, PermissionEffect.Grant);
            await harness.AddGroupRightAsync(adminRoleName, Permissions.AdminRolesManage, PermissionEffect.Grant);
            await harness.AddGroupRightAsync(adminRoleName, Permissions.KpiView, PermissionEffect.Grant);
            await harness.AddMembershipAsync(administrator, adminRoleName);

            harness.AdministratorService = new TestCurrentUserService
            {
                UserId = administrator,
                UserName = "administrator",
                Permissions = new[]
                {
                    Permissions.AdminAccessGroupsManage,
                    Permissions.AdminRightsManage,
                    Permissions.AdminUserRightsManage,
                },
            };

            harness.Handlers = new AccessControlCommandHandlers(
                harness.Rbac,
                new GenericRepository<Role>(harness.Context),
                new GenericRepository<RolePermission>(harness.Context),
                new GenericRepository<UserRole>(harness.Context),
                new GenericRepository<Permission>(harness.Context),
                new GenericRepository<UserRight>(harness.Context),
                new GenericRepository<User>(harness.Context),
                harness.AdministratorService,
                new AuditContext());
            return harness;
        }

        private async Task SeedAsync()
        {
            foreach (var name in new[]
            {
                Permissions.KpiView,
                Permissions.KpiEdit,
                Permissions.AdminUsersManage,
                Permissions.AdminRolesManage
            })
            {
                Context.Permissions.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Module = name.Split(':')[0],
                    Status = EntityStatus.Active,
                    CreatedBy = Guid.NewGuid(),
                    CreatedDate = DateTime.UtcNow,
                });
            }

            foreach (var (name, description) in new[]
            {
                ("Admin", "System administration role"),
                ("Administration", "Full administration rights"),
                ("KPI Readers", "Read-only reporting"),
                ("Power Users", "Configuration owner"),
                ("Executive", "Executive overview"),
            })
            {
                Context.Roles.Add(new Role
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Description = description,
                    Status = EntityStatus.Active,
                    CreatedBy = Guid.NewGuid(),
                    CreatedDate = DateTime.UtcNow,
                });
            }

            foreach (var (username, fullName) in new[]
            {
                ("administrator", "System Administrator"),
                ("olivia", "Olivia Carmichael"),
                ("coordinator", "Division Coordinator"),
            })
            {
                Context.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Username = username,
                    FullName = fullName,
                    PasswordHash = "test-hash",
                    IsActive = true,
                    Status = EntityStatus.Active,
                    CreatedBy = Guid.NewGuid(),
                    CreatedDate = DateTime.UtcNow,
                });
            }

            await Context.SaveChangesAsync();
        }

        public async Task<Guid> GetUserIdAsync(string username) =>
            await Context.Users.Where(u => u.Username == username)
                .Select(u => u.Id)
                .FirstAsync();

        public async Task<Guid> GetRoleIdAsync(string roleName) =>
            await Context.Roles.Where(r => r.Name == roleName).Select(r => r.Id).FirstAsync();

        public async Task<Guid> GetPermissionIdAsync(string name) =>
            await Context.Permissions.Where(p => p.Name == name).Select(p => p.Id).FirstAsync();

        public async Task<Guid> AddMembershipAsync(Guid userId, string roleName)
        {
            var membership = new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = await GetRoleIdAsync(roleName),
                Status = EntityStatus.Active,
                CreatedBy = Guid.NewGuid(),
                CreatedDate = DateTime.UtcNow,
            };
            Context.UserRoles.Add(membership);
            await Context.SaveChangesAsync();
            return membership.Id;
        }

        public async Task SoftDeleteMembershipAsync(Guid membershipId)
        {
            var row = await Context.UserRoles.FirstAsync(ur => ur.Id == membershipId);
            row.IsDeleted = true;
            row.UpdatedDate = DateTime.UtcNow;
            await Context.SaveChangesAsync();
        }

        public async Task RestoreMembershipAsync(Guid membershipId)
        {
            var row = await Context.UserRoles.IgnoreQueryFilters().FirstAsync(ur => ur.Id == membershipId);
            row.IsDeleted = false;
            row.UpdatedDate = DateTime.UtcNow;
            await Context.SaveChangesAsync();
        }

        public async Task RemoveMembershipAsync(string username, string roleName)
        {
            var userId = await GetUserIdAsync(username);
            var roleId = await GetRoleIdAsync(roleName);
            var membership = await Context.UserRoles
                .FirstAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
            Context.UserRoles.Remove(membership);
            await Context.SaveChangesAsync();
        }

        public async Task AddGroupRightAsync(string roleName, string permissionName, PermissionEffect effect)
        {
            Context.RolePermissions.Add(new RolePermission
            {
                RoleId = await GetRoleIdAsync(roleName),
                PermissionId = await GetPermissionIdAsync(permissionName),
                Effect = effect,
                Status = EntityStatus.Active,
                CreatedBy = Guid.NewGuid(),
                CreatedDate = DateTime.UtcNow,
            });
            await Context.SaveChangesAsync();
        }

        public async Task DeactivateRoleAsync(string roleName)
        {
            var role = await Context.Roles.IgnoreQueryFilters().FirstAsync(r => r.Name == roleName);
            role.Status = EntityStatus.Inactive;
            role.UpdatedDate = DateTime.UtcNow;
            await Context.SaveChangesAsync();
        }

        public async Task AddUserRightAsync(Guid userId, string permissionName, PermissionEffect effect)
        {
            Context.UserRights.Add(new UserRight
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PermissionId = await GetPermissionIdAsync(permissionName),
                Effect = effect,
                Reason = "test",
                Status = EntityStatus.Active,
                CreatedBy = Guid.NewGuid(),
                CreatedDate = DateTime.UtcNow,
            });
            await Context.SaveChangesAsync();
        }

        public async Task DeactivateUserAsync(Guid userId)
        {
            var user = await Context.Users.FirstAsync(u => u.Id == userId);
            user.IsActive = false;
            user.Status = EntityStatus.Inactive;
            user.UpdatedDate = DateTime.UtcNow;
            await Context.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => lease.DisposeAsync();
}
