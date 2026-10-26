using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Constants;

namespace Application.Tests.Divisions;

/// <summary>
/// H1 regression coverage: organizational scope must resolve corporate visibility from the
/// live authorization state, never from the permission claims minted into the JWT.
/// </summary>
public class OrganizationalScopeServiceTests
{
    private static readonly Guid userId = Guid.NewGuid();

    [Fact]
    public async Task HasCorporateAccess_StaleJwtClaimWithoutLiveGrant_IsDenied()
    {
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            Permissions = new[] { Permissions.PerformanceMonitorView }
        };
        var effective = new TestEffectivePermissionService();

        var scope = new OrganizationalScopeService(currentUser, effective);

        Assert.False(await scope.HasCorporateAccessAsync());
    }

    [Fact]
    public async Task HasCorporateAccess_LiveGrantWithoutJwtClaim_IsAllowed()
    {
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            Permissions = Array.Empty<string>()
        };
        var effective = new TestEffectivePermissionService();
        effective.Grant(userId, Permissions.PerformanceMonitorView);

        var scope = new OrganizationalScopeService(currentUser, effective);

        Assert.True(await scope.HasCorporateAccessAsync());
    }

    [Fact]
    public async Task EnsureDivisionAsync_DivisionPrincipalOtherDivision_ThrowsForbidden()
    {
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            DivisionId = Guid.NewGuid(),
            Permissions = new[] { Permissions.PerformanceMonitorView }
        };
        var scope = new OrganizationalScopeService(currentUser, new TestEffectivePermissionService());

        await Assert.ThrowsAsync<ForbiddenException>(
            () => scope.EnsureDivisionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task EnsureDivisionAsync_CorporatePrincipal_AllowsAnyDivision()
    {
        var currentUser = new TestCurrentUserService { UserId = userId };
        var effective = new TestEffectivePermissionService();
        effective.Grant(userId, Permissions.PerformanceMonitorView);
        var scope = new OrganizationalScopeService(currentUser, effective);

        await scope.EnsureDivisionAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task HasCorporateAccess_AnonymousPrincipal_IsDenied()
    {
        var scope = new OrganizationalScopeService(
            new TestCurrentUserService(),
            new TestEffectivePermissionService());

        Assert.False(await scope.HasCorporateAccessAsync());
    }
}
