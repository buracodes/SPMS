using Application.CQRS.KpiActuals;
using Application.Exceptions;
using Application.Tests.TestSupport;
using Domain.Constants;
using Domain.Entities;

namespace Application.Tests.Kpis;

/// <summary>
/// The write guard is the ownership half of actual-entry authorization; the endpoint policy
/// supplies the permission half and the lock behavior supplies the period half. These tests
/// lock the guard's contract: division membership alone grants nothing, and the override
/// right is evaluated from the live grant state on every call.
/// </summary>
public class KpiActualWriteGuardTests
{
    private static Kpi KpiWith(Guid? divisionOwnerId = null, Guid? ownerId = null) => new()
    {
        Id = Guid.NewGuid(),
        ScorecardId = Guid.NewGuid(),
        Name = "Systems Delivered",
        Weight = 100,
        DivisionOwnerId = divisionOwnerId,
        OwnerId = ownerId
    };

    [Fact]
    public async Task DivisionAccount_ForeignDivisionKpi_IsForbidden()
    {
        var callerDivision = Guid.NewGuid();
        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            DivisionId = callerDivision
        };
        var kpi = KpiWith(divisionOwnerId: Guid.NewGuid());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            KpiActualWriteGuard.EnsureWriteAllowedAsync(
                kpi, user, new TestEffectivePermissionService(), CancellationToken.None));
    }

    [Fact]
    public async Task DivisionAccount_UnassignedKpi_IsForbidden()
    {
        // Merely belonging to a division must not be treated as ownership of every KPI
        // in that division's plan.
        var user = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            DivisionId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            KpiActualWriteGuard.EnsureWriteAllowedAsync(
                KpiWith(), user, new TestEffectivePermissionService(), CancellationToken.None));
    }

    [Fact]
    public async Task DivisionAccount_OwnDivisionKpi_IsAllowed()
    {
        var divisionId = Guid.NewGuid();
        var user = new TestCurrentUserService { UserId = Guid.NewGuid(), DivisionId = divisionId };

        await KpiActualWriteGuard.EnsureWriteAllowedAsync(
            KpiWith(divisionOwnerId: divisionId), user, new TestEffectivePermissionService(), CancellationToken.None);
    }

    [Fact]
    public async Task DivisionAccount_UserOwnedKpi_IsAllowed()
    {
        var userId = Guid.NewGuid();
        var user = new TestCurrentUserService { UserId = userId, DivisionId = Guid.NewGuid() };

        await KpiActualWriteGuard.EnsureWriteAllowedAsync(
            KpiWith(ownerId: userId), user, new TestEffectivePermissionService(), CancellationToken.None);
    }

    [Fact]
    public async Task LiveOverrideGrant_AllowsCrossDivisionCorrection_AndRevocationClosesIt()
    {
        var userId = Guid.NewGuid();
        var user = new TestCurrentUserService { UserId = userId, DivisionId = Guid.NewGuid() };
        var kpi = KpiWith(divisionOwnerId: Guid.NewGuid());

        var live = new TestEffectivePermissionService();
        live.Grant(userId, Permissions.ActualsOverride);

        await KpiActualWriteGuard.EnsureWriteAllowedAsync(kpi, user, live, CancellationToken.None);

        // A revoked override grant must stop applying on the very next call, even though
        // the caller's division claim and any stale JWT permission claim are unchanged.
        live.Revoke(userId, Permissions.ActualsOverride);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            KpiActualWriteGuard.EnsureWriteAllowedAsync(kpi, user, live, CancellationToken.None));
    }

    [Fact]
    public async Task CorporatePrincipal_IsFencedByPermissionsAndLocks_NotTheDivisionGuard()
    {
        var user = new TestCurrentUserService { UserId = Guid.NewGuid(), Permissions = Array.Empty<string>() };

        await KpiActualWriteGuard.EnsureWriteAllowedAsync(
            KpiWith(divisionOwnerId: Guid.NewGuid()), user, new TestEffectivePermissionService(), CancellationToken.None);
    }
}
