using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence.Seed;
using Infrastructure.Service;
using Microsoft.EntityFrameworkCore;
using Application.Tests.TestSupport;

namespace Application.Tests.Persistence;

public class DivisionAccountSeederTests
{
    [Fact]
    public async Task SeedDivisionAccounts_CreatesOneAccountPerDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var roleId = await SeedDataInputRoleAsync(lease);
        var itDivision = await SeedDivisionAsync(lease, "Information Technology", "IT");
        var tradingDivision = await SeedDivisionAsync(lease, "Trading Operation", "TRO");

        var hasher = new PasswordHasherService();
        await SpmsDbSeeder.SeedDivisionAccountsAsync(lease.Context, hasher);

        var itAccount = await lease.Context.Users
            .Include(u => u.UserRoles)
            .SingleOrDefaultAsync(u => u.DivisionId == itDivision.Id);
        var tradingAccount = await lease.Context.Users
            .Include(u => u.UserRoles)
            .SingleOrDefaultAsync(u => u.DivisionId == tradingDivision.Id);

        Assert.NotNull(itAccount);
        Assert.Equal("IT", itAccount.Username);
        Assert.Equal("Information Technology Division Account", itAccount.FullName);
        Assert.True(itAccount.IsActive);
        Assert.True(hasher.VerifyPassword(itAccount.PasswordHash, "IT"));
        Assert.Contains(itAccount.UserRoles, ur => ur.RoleId == roleId);

        Assert.NotNull(tradingAccount);
        Assert.Equal("TRO", tradingAccount.Username);
        Assert.True(hasher.VerifyPassword(tradingAccount.PasswordHash, "TRO"));
    }

    [Fact]
    public async Task SeedDivisionAccounts_RunsTwice_StillOneAccountPerDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDataInputRoleAsync(lease);
        var itDivision = await SeedDivisionAsync(lease, "Information Technology", "IT");

        var hasher = new PasswordHasherService();
        await SpmsDbSeeder.SeedDivisionAccountsAsync(lease.Context, hasher);
        await SpmsDbSeeder.SeedDivisionAccountsAsync(lease.Context, hasher);

        var accountCount = await lease.Context.Users.CountAsync(u => u.DivisionId == itDivision.Id);
        Assert.Equal(1, accountCount);
        Assert.Equal(1, await lease.Context.Users.CountAsync());
    }

    [Fact]
    public async Task SeedDivisionAccounts_SkipsDivisionWhoseCodeIsTakenByExistingUser()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDataInputRoleAsync(lease);
        var itDivision = await SeedDivisionAsync(lease, "Information Technology", "IT");

        lease.Context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "IT",
            FullName = "Existing Corporate User",
            PasswordHash = "hash",
            IsActive = true
        });
        await lease.Context.SaveChangesAsync();

        await SpmsDbSeeder.SeedDivisionAccountsAsync(lease.Context, new PasswordHasherService());

        Assert.Equal(1, await lease.Context.Users.CountAsync());
        Assert.False(await lease.Context.Users.AnyAsync(u => u.DivisionId == itDivision.Id));
    }

    [Fact]
    public async Task SeedDivisionAccounts_DoesNothingWhenNoDivisionsExist()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDataInputRoleAsync(lease);

        await SpmsDbSeeder.SeedDivisionAccountsAsync(lease.Context, new PasswordHasherService());

        Assert.Equal(0, await lease.Context.Users.CountAsync());
    }

    [Fact]
    public async Task SeedDivisionAccounts_DoesNothingWhenDataInputRoleMissing()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDivisionAsync(lease, "Information Technology", "IT");

        await SpmsDbSeeder.SeedDivisionAccountsAsync(lease.Context, new PasswordHasherService());

        Assert.Equal(0, await lease.Context.Users.CountAsync());
    }

    private static async Task<Guid> SeedDataInputRoleAsync(TestDbContextLease lease)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = Roles.DataInputUser,
            Description = "Default Data Input User role."
        };
        lease.Context.Roles.Add(role);
        await lease.Context.SaveChangesAsync();
        return role.Id;
    }

    private static async Task<Division> SeedDivisionAsync(TestDbContextLease lease, string name, string code)
    {
        var division = new Division { Id = Guid.NewGuid(), Name = name, Code = code };
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();
        return division;
    }
}
