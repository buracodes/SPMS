using Application.CQRS.Auth.Handlers.Commands;
using Application.CQRS.Auth.Requests.Commands;
using Application.DTOS.Auth;
using Application.Exceptions;
using Application.Tests.TestSupport;
using Application.Validation.Auth;
using Domain.Entities;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Service;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Application.Tests.Auth;

public class PasswordManagementHandlerTests
{
    private static ChangePasswordCommandHandler CreateChangeHandler(
        TestDbContextLease lease,
        TestCurrentUserService currentUser)
        => new(
            new UserRepository(lease.Context),
            new RefreshTokenRepository(lease.Context),
            new PasswordHasherService(),
            currentUser);

    private static ResetDivisionPasswordCommandHandler CreateResetHandler(TestDbContextLease lease)
        => new(
            new UserRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new RefreshTokenRepository(lease.Context),
            new PasswordHasherService());

    private static async Task<User> SeedUserAsync(
        TestDbContextLease lease,
        string username,
        string password,
        Guid? divisionId = null,
        int failedLoginCount = 0,
        DateTime? lockedUntilUtc = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = $"{username} account",
            PasswordHash = new PasswordHasherService().HashPassword(password),
            IsActive = true,
            DivisionId = divisionId,
            FailedLoginCount = failedLoginCount,
            LockedUntilUtc = lockedUntilUtc
        };
        lease.Context.Users.Add(user);
        await lease.Context.SaveChangesAsync();
        return user;
    }

    private static async Task<RefreshToken> SeedRefreshTokenAsync(TestDbContextLease lease, Guid userId, bool expired = false)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expired ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(7)
        };
        lease.Context.RefreshTokens.Add(token);
        await lease.Context.SaveChangesAsync();
        return token;
    }

    private static ChangePasswordCommandRequest ChangeRequest(string current, string next)
        => new() { Dto = new ChangePasswordDto { CurrentPassword = current, NewPassword = next, ConfirmPassword = next } };

    private static ResetDivisionPasswordCommandRequest ResetRequest(Guid divisionId, string next)
        => new() { Dto = new ResetDivisionPasswordDto { DivisionId = divisionId, NewPassword = next, ConfirmPassword = next } };

    [Fact]
    public async Task ChangePassword_ValidCurrentPassword_UpdatesHashAndRevokesSessions()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(lease, "IT", "IT");
        var session = await SeedRefreshTokenAsync(lease, user.Id);

        var handler = CreateChangeHandler(lease, new TestCurrentUserService { UserId = user.Id });
        var response = await handler.Handle(ChangeRequest("IT", "Str0ng@Pass"), default);

        Assert.True(response.Success);
        lease.Context.ChangeTracker.Clear();

        var updated = await lease.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(new PasswordHasherService().VerifyPassword(updated.PasswordHash, "Str0ng@Pass"));

        var token = await lease.Context.RefreshTokens.SingleAsync(t => t.Id == session.Id);
        Assert.NotNull(token.RevokedAtUtc);
        Assert.Equal("Password changed by user", token.ReasonRevoked);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(lease, "IT", "IT");

        var handler = CreateChangeHandler(lease, new TestCurrentUserService { UserId = user.Id });

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(ChangeRequest("wrong", "Str0ng@Pass"), default));

        Assert.Equal("Current password is incorrect.", ex.Message);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordMatchesUsername_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(lease, "it", "Old@Pass1");

        var handler = CreateChangeHandler(lease, new TestCurrentUserService { UserId = user.Id });

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(ChangeRequest("Old@Pass1", "IT"), default));

        Assert.Equal("New password must not match the username.", ex.Message);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordSameAsCurrent_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(lease, "planner", "Str0ng@Pass");

        var handler = CreateChangeHandler(lease, new TestCurrentUserService { UserId = user.Id });

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(ChangeRequest("Str0ng@Pass", "Str0ng@Pass"), default));

        Assert.Equal("New password must be different from the current password.", ex.Message);
    }

    [Fact]
    public async Task ChangePassword_LockedAccount_ClearsLockoutState()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(
            lease,
            "IT",
            "IT",
            failedLoginCount: 3,
            lockedUntilUtc: DateTime.UtcNow.AddMinutes(15));

        var handler = CreateChangeHandler(lease, new TestCurrentUserService { UserId = user.Id });
        await handler.Handle(ChangeRequest("IT", "Str0ng@Pass"), default);

        lease.Context.ChangeTracker.Clear();

        var updated = await lease.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, updated.FailedLoginCount);
        Assert.Null(updated.LockedUntilUtc);
    }

    [Fact]
    public async Task ChangePassword_LeavesAlreadyExpiredSessionsAlone()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(lease, "IT", "IT");
        var expired = await SeedRefreshTokenAsync(lease, user.Id, expired: true);

        var handler = CreateChangeHandler(lease, new TestCurrentUserService { UserId = user.Id });
        await handler.Handle(ChangeRequest("IT", "Str0ng@Pass"), default);

        lease.Context.ChangeTracker.Clear();

        var token = await lease.Context.RefreshTokens.SingleAsync(t => t.Id == expired.Id);
        Assert.Null(token.RevokedAtUtc);
    }

    [Fact]
    public async Task ChangePassword_NoAuthenticatedUser_ThrowsUnauthorized()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var handler = CreateChangeHandler(lease, new TestCurrentUserService());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(ChangeRequest("IT", "Str0ng@Pass"), default));
    }

    [Fact]
    public async Task ResetDivisionPassword_ResetsAccountAndRevokesSessions()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var division = new Division { Id = Guid.NewGuid(), Name = "Information Technology", Code = "IT" };
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();

        var account = await SeedUserAsync(lease, "IT", "IT", division.Id);
        var session = await SeedRefreshTokenAsync(lease, account.Id);

        var handler = CreateResetHandler(lease);
        var response = await handler.Handle(ResetRequest(division.Id, "Str0ng@Pass"), default);

        Assert.True(response.Success);
        Assert.Contains("Information Technology", response.Message);
        lease.Context.ChangeTracker.Clear();

        var updated = await lease.Context.Users.SingleAsync(u => u.Id == account.Id);
        Assert.True(new PasswordHasherService().VerifyPassword(updated.PasswordHash, "Str0ng@Pass"));

        var token = await lease.Context.RefreshTokens.SingleAsync(t => t.Id == session.Id);
        Assert.NotNull(token.RevokedAtUtc);
        Assert.Equal("Password reset by administrator", token.ReasonRevoked);
    }

    [Fact]
    public async Task ResetDivisionPassword_UnknownDivision_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var handler = CreateResetHandler(lease);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(ResetRequest(Guid.NewGuid(), "Str0ng@Pass"), default));

        Assert.Equal("Division was not found.", ex.Message);
    }

    [Fact]
    public async Task ResetDivisionPassword_DivisionWithoutAccount_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var division = new Division { Id = Guid.NewGuid(), Name = "Trading Operation", Code = "TRO" };
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();

        var handler = CreateResetHandler(lease);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(ResetRequest(division.Id, "Str0ng@Pass"), default));

        Assert.Equal("Division account for 'Trading Operation' was not found.", ex.Message);
    }

    [Fact]
    public async Task ResetDivisionPassword_PasswordMatchesUsername_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var division = new Division { Id = Guid.NewGuid(), Name = "Information Technology", Code = "IT" };
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();
        await SeedUserAsync(lease, "IT", "IT", division.Id);

        var handler = CreateResetHandler(lease);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(ResetRequest(division.Id, "it"), default));

        Assert.Equal("New password must not match the username.", ex.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("short1!", false)]
    [InlineData("alllowercase1!", false)]
    [InlineData("ALLUPPERCASE1!", false)]
    [InlineData("NoDigitsHere!", false)]
    [InlineData("NoSymbols12345", false)]
    [InlineData("Str0ng@Pass", true)]
    public void PasswordPolicy_AcceptsOnlyStrongPasswords(string? candidate, bool expected)
    {
        Assert.Equal(expected, PasswordPolicy.IsSatisfiedBy(candidate));
    }

    [Fact]
    public void ChangePasswordValidator_RejectsWeakAndMismatchedPasswords()
    {
        var validator = new ChangePasswordValidator();

        Assert.False(validator.Validate(ChangeRequest("IT", "weak")).IsValid);
        Assert.False(validator.Validate(new ChangePasswordCommandRequest
        {
            Dto = new ChangePasswordDto { CurrentPassword = "IT", NewPassword = "Str0ng@Pass", ConfirmPassword = "Other@Pass1" }
        }).IsValid);
        Assert.True(validator.Validate(ChangeRequest("IT", "Str0ng@Pass")).IsValid);
    }

    [Fact]
    public void ResetDivisionPasswordValidator_RequiresDivisionAndMatchingConfirmation()
    {
        var validator = new ResetDivisionPasswordValidator();

        Assert.False(validator.Validate(ResetRequest(Guid.Empty, "Str0ng@Pass")).IsValid);
        Assert.False(validator.Validate(ResetRequest(Guid.NewGuid(), "weak")).IsValid);
        Assert.False(validator.Validate(new ResetDivisionPasswordCommandRequest
        {
            Dto = new ResetDivisionPasswordDto { DivisionId = Guid.NewGuid(), NewPassword = "Str0ng@Pass", ConfirmPassword = "Other@Pass1" }
        }).IsValid);
        Assert.True(validator.Validate(ResetRequest(Guid.NewGuid(), "Str0ng@Pass")).IsValid);
    }
}
