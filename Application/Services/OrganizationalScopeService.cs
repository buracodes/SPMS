using Application.Exceptions;
using Application.Interface.IService;
using Domain.Constants;
using System;
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

    public Guid? DivisionId => currentUserService.DivisionId;

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

    public async Task<bool> CanAccessDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
    {
        // The division claim is the cheapest and most specific check, so try it first and
        // only consult the live permission state for cross-division access.
        if (DivisionId.HasValue && DivisionId.Value == divisionId)
        {
            return true;
        }

        return await HasCorporateAccessAsync(cancellationToken);
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
        if (!DivisionId.HasValue)
        {
            return await HasCorporateAccessAsync(cancellationToken);
        }

        var userId = currentUserService.UserId;
        var userOwned = ownerId.HasValue && userId.HasValue && ownerId.Value == userId.Value;
        var divisionOwned = divisionOwnerId.HasValue && divisionOwnerId.Value == DivisionId.Value;

        return userOwned || divisionOwned;
    }
}
