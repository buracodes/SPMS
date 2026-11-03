using Application.Exceptions;
using Application.Interface.IService;
using Domain.Constants;
using System.Linq;

namespace Application.CQRS.KpiActuals;

/// <summary>
/// FR-305/FR-906: a division account may write actual values only for KPIs it is responsible
/// for (its division or its own user assignment). Unassigned or foreign KPIs are rejected, so
/// a group-granted actual-entry right never widens a division employee past their scope.
/// Corporate principals keep the existing role-based rules. The override right is resolved
/// live, so a revoked grant stops applying on the next request.
/// </summary>
public static class KpiActualWriteGuard
{
    public static async Task EnsureWriteAllowedAsync(
        Domain.Entities.Kpi kpi,
        ICurrentUserService currentUserService,
        IEffectivePermissionService effectivePermissionService,
        CancellationToken cancellationToken = default)
    {
        var divisionScope = currentUserService.DivisionIds;
        if (divisionScope.Count == 0)
        {
            return;
        }

        if (currentUserService.UserId is Guid userId
            && await effectivePermissionService.HasPermissionAsync(
                userId, Permissions.ActualsOverride, cancellationToken))
        {
            return;
        }

        var divisionOwned = kpi.DivisionOwnerId.HasValue
            && divisionScope.Contains(kpi.DivisionOwnerId.Value);
        var userOwned = kpi.OwnerId.HasValue
            && currentUserService.UserId.HasValue
            && kpi.OwnerId.Value == currentUserService.UserId.Value;

        if (!divisionOwned && !userOwned)
        {
            throw new ForbiddenException("A division account can only enter actual values for the KPIs assigned to its division or owner.");
        }
    }
}
