using Application.Exceptions;
using Application.Interface.IService;
using Domain.Constants;

namespace Application.CQRS.KpiActuals;

/// <summary>
/// FR-305/FR-906: a division account may write actual values only for KPIs it is responsible
/// for (its division or its own user assignment). Legacy KPIs with no owner stay writable so
/// existing data entry is not broken. Corporate principals keep the existing role-based rules.
/// </summary>
public static class KpiActualWriteGuard
{
    public static void EnsureWriteAllowed(Domain.Entities.Kpi kpi, ICurrentUserService currentUserService)
    {
        if (!currentUserService.DivisionId.HasValue)
        {
            return;
        }

        if (currentUserService.Permissions.Contains(Permissions.ActualsOverride, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var division = currentUserService.DivisionId.Value;
        var divisionOwned = kpi.DivisionOwnerId.HasValue && kpi.DivisionOwnerId.Value == division;
        var userOwned = kpi.OwnerId.HasValue
            && currentUserService.UserId.HasValue
            && kpi.OwnerId.Value == currentUserService.UserId.Value;
        var unassigned = !kpi.OwnerId.HasValue && !kpi.DivisionOwnerId.HasValue;

        if (!divisionOwned && !userOwned && !unassigned)
        {
            throw new ForbiddenException("A division account can only enter actual values for the KPIs assigned to its division or owner.");
        }
    }
}
