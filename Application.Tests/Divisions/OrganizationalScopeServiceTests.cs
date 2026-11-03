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

    [Fact]
    public async Task GroupGrantedPortfolioRights_DoNotWidenDivisionPrincipal()
    {
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            DivisionId = Guid.NewGuid()
        };
        var effective = new TestEffectivePermissionService();
        effective.Grant(userId, Permissions.DivisionView);
        effective.Grant(userId, Permissions.PerformanceMonitorView);
        var scope = new OrganizationalScopeService(currentUser, effective);

        Assert.False(await scope.HasDivisionPortfolioAccessAsync());
        await Assert.ThrowsAsync<ForbiddenException>(
            () => scope.EnsureDivisionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GroupGrantedInitiativeView_DoesNotWidenDivisionPrincipal()
    {
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            DivisionId = Guid.NewGuid()
        };
        var effective = new TestEffectivePermissionService();
        effective.Grant(userId, Permissions.InitiativesView);
        var scope = new OrganizationalScopeService(currentUser, effective);

        Assert.False(await scope.CanAccessInitiativePortfolioAsync());
        await Assert.ThrowsAsync<ForbiddenException>(
            () => scope.EnsureInitiativeAsync(null, Guid.NewGuid()));
    }

    [Fact]
    public async Task MultiDivisionScope_AccessesEveryAuthorizedDivisionOnly()
    {
        var divisionA = Guid.NewGuid();
        var divisionB = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            DivisionIds = new[] { divisionA, divisionB }
        };
        var scope = new OrganizationalScopeService(currentUser, new TestEffectivePermissionService());

        Assert.Equal(new[] { divisionA, divisionB }, scope.DivisionIds);
        Assert.True(await scope.CanAccessDivisionAsync(divisionA));
        Assert.True(await scope.CanAccessDivisionAsync(divisionB));
        Assert.False(await scope.CanAccessDivisionAsync(sibling));
        Assert.False(await scope.HasDivisionPortfolioAccessAsync());

        await scope.EnsureDivisionAsync(divisionA);
        await scope.EnsureDivisionAsync(divisionB);
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.EnsureDivisionAsync(sibling));
    }

    [Fact]
    public async Task MultiDivisionScope_AssignmentCoversEveryAuthorizedDivision()
    {
        var divisionA = Guid.NewGuid();
        var divisionB = Guid.NewGuid();
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            DivisionIds = new[] { divisionA, divisionB }
        };
        var scope = new OrganizationalScopeService(currentUser, new TestEffectivePermissionService());

        Assert.True(await scope.IsAssignedKpiAsync(null, divisionA));
        Assert.True(await scope.IsAssignedKpiAsync(null, divisionB));
        Assert.False(await scope.IsAssignedKpiAsync(null, Guid.NewGuid()));
        Assert.False(await scope.IsAssignedKpiAsync(Guid.NewGuid(), null));
        Assert.True(await scope.IsAssignedKpiAsync(userId, null));
    }

    [Fact]
    public async Task BroadReadGrant_WithSingleDivisionScope_DoesNotWidenToSiblingDivision()
    {
        var divisionA = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            DivisionId = divisionA
        };
        var effective = new TestEffectivePermissionService();
        effective.Grant(userId, Permissions.DivisionView);
        effective.Grant(userId, Permissions.PerformanceMonitorView);
        var scope = new OrganizationalScopeService(currentUser, effective);

        Assert.True(await scope.CanAccessDivisionAsync(divisionA));
        Assert.False(await scope.CanAccessDivisionAsync(sibling));
        Assert.False(await scope.HasDivisionPortfolioAccessAsync());
    }

    [Fact]
    public async Task NoDivisionScope_CorporatePrincipal_AccessesAnyDivision()
    {
        var effective = new TestEffectivePermissionService();
        effective.Grant(userId, Permissions.PerformanceMonitorView);
        var scope = new OrganizationalScopeService(
            new TestCurrentUserService { UserId = userId },
            effective);

        Assert.Empty(scope.DivisionIds);
        Assert.True(await scope.CanAccessDivisionAsync(Guid.NewGuid()));
    }
}
