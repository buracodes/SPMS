using Application.Exceptions;
using Application.Interface.IService;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.CQRS.KpiActuals;

/// <summary>
/// FR-305: a division account reads actual values only for the KPIs assigned to its
/// division (DivisionOwnerId) or for KPIs whose individual owner is the principal
/// itself. Explicitly authorized organization-wide visibility (the live
/// performance:monitor.view grant) is evaluated first, so Admin and Power User accounts
/// — which also carry an HR division claim — are not fenced by the ordinary division
/// ownership restriction. Corporate principals keep the existing role-based visibility.
/// </summary>
public static class KpiActualReadGuard
{
    public static async Task EnsureReadAllowedAsync(
        Domain.Entities.Kpi kpi,
        ICurrentUserService currentUserService,
        IOrganizationalScopeService organizationalScope,
        CancellationToken cancellationToken = default)
    {
        var divisionScope = currentUserService.DivisionIds;
        if (divisionScope.Count == 0)
        {
            return;
        }

        if (await organizationalScope.HasCorporateAccessAsync(cancellationToken))
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
            throw new ForbiddenException("A division account can only view actual values for the KPIs assigned to its division or owner.");
        }
    }
}
