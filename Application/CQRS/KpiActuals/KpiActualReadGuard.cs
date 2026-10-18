using Application.Exceptions;
using Application.Interface.IService;

namespace Application.CQRS.KpiActuals;

/// <summary>
/// FR-305: a division account reads actual values only for the KPIs assigned to its
/// division (DivisionOwnerId) or for KPIs whose individual owner is the principal
/// itself. Corporate principals (no division claim) keep the role-based visibility.
/// </summary>
public static class KpiActualReadGuard
{
    public static void EnsureReadAllowed(Domain.Entities.Kpi kpi, ICurrentUserService currentUserService)
    {
        if (!currentUserService.DivisionId.HasValue)
        {
            return;
        }

        var division = currentUserService.DivisionId.Value;
        var divisionOwned = kpi.DivisionOwnerId.HasValue && kpi.DivisionOwnerId.Value == division;
        var userOwned = kpi.OwnerId.HasValue
            && currentUserService.UserId.HasValue
            && kpi.OwnerId.Value == currentUserService.UserId.Value;

        if (!divisionOwned && !userOwned)
        {
            throw new ForbiddenException("A division account can only view actual values for the KPIs assigned to its division or owner.");
        }
    }
}
