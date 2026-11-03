using Application.Exceptions;
using Application.Interface.IService;
using Domain.Constants;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services;

public class OrganizationalScopeService : IOrganizationalScopeService
{
    private readonly ICurrentUserService currentUserService;
    private readonly IEffectivePermissionService effectivePermissions;

    public OrganizationalScopeService(
        ICurrentUserService currentUserService,
        IEffectivePermissionService effectivePermissions)
    {
        this.currentUserService = currentUserService;
        this.effectivePermissions = effectivePermissions;
    }

    public Guid? UserId => currentUserService.UserId;

    public Guid? DivisionId => currentUserService.DivisionId;

    public IReadOnlyList<Guid> DivisionIds => currentUserService.DivisionIds;

    private bool HasDivisionScope => currentUserService.DivisionIds.Count > 0;

    private bool IsInDivisionScope(Guid? divisionId)
        => divisionId.HasValue && currentUserService.DivisionIds.Contains(divisionId.Value);

    /// <summary>
    /// Corporate across-division visibility is evaluated against the live authorization
    /// state, not the permission claims in the JWT. This keeps data scoping consistent with
    /// <c>PermissionAuthorizationHandler</c> (ADR-011): a revoked grant stops granting access
    /// on the next request instead of when the token happens to be refreshed.
    /// </summary>
    public async Task<bool> HasCorporateAccessAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return false;
        }

        return await effectivePermissions.HasPermissionAsync(
            userId.Value,
            Permissions.PerformanceMonitorView,
            cancellationToken);
    }

    public async Task<bool> HasDivisionPortfolioAccessAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return false;
        }

        // The division scope is authoritative and always wins: division accounts only ever
        // see their authorized divisions, even if a broad read permission is also granted.
        if (HasDivisionScope)
        {
            return false;
        }

        if (await effectivePermissions.HasPermissionAsync(
                userId.Value,
                Permissions.PerformanceMonitorView,
                cancellationToken))
        {
            return true;
        }

        // FR-908 / Module 9 matrix: View Only roles can select a division and read the
        // corporate goals of its plan without holding performance:monitor.view.
        return await effectivePermissions.HasPermissionAsync(
            userId.Value,
            Permissions.DivisionView,
            cancellationToken);
    }

    public async Task<bool> CanAccessDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
    {
        // The division scope is the cheapest and most specific check, so try it first and
        // only consult the live permission state for cross-division access. An explicitly
        // authorized division is accepted exactly like the primary HR division.
        if (currentUserService.DivisionIds.Contains(divisionId))
        {
            return true;
        }

        return await HasDivisionPortfolioAccessAsync(cancellationToken);
    }

    public async Task EnsureDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessDivisionAsync(divisionId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this division's data.");
        }
    }

    public async Task<bool> IsAssignedKpiAsync(
        Guid? ownerId,
        Guid? divisionOwnerId,
        CancellationToken cancellationToken = default)
    {
        if (!HasDivisionScope)
        {
            return await HasCorporateAccessAsync(cancellationToken);
        }

        var userId = currentUserService.UserId;
        var userOwned = ownerId.HasValue && userId.HasValue && ownerId.Value == userId.Value;
        var divisionOwned = IsInDivisionScope(divisionOwnerId);

        return userOwned || divisionOwned;
    }

    public async Task<bool> CanAccessInitiativePortfolioAsync(CancellationToken cancellationToken = default)
    {
        if (HasDivisionScope)
        {
            return false;
        }

        var userId = currentUserService.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return false;
        }

        return await effectivePermissions.HasPermissionAsync(
            userId.Value,
            Permissions.InitiativesView,
            cancellationToken);
    }

    public async Task<bool> CanAccessInitiativeAsync(
        Guid? ownerId,
        Guid? divisionOwnerId,
        CancellationToken cancellationToken = default)
    {
        // A division scope is authoritative: even a group-granted read right never widens the
        // employee beyond their own initiatives and their authorized divisions' initiatives.
        if (!HasDivisionScope)
        {
            return await CanAccessInitiativePortfolioAsync(cancellationToken);
        }

        var userId = currentUserService.UserId;
        var userOwned = ownerId.HasValue && userId.HasValue && ownerId.Value == userId.Value;
        var divisionOwned = IsInDivisionScope(divisionOwnerId);

        return userOwned || divisionOwned;
    }

    public async Task EnsureInitiativeAsync(
        Guid? ownerId,
        Guid? divisionOwnerId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessInitiativeAsync(ownerId, divisionOwnerId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this initiative.");
        }
    }
}
