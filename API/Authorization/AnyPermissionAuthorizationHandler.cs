using Application.Interface.IService;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace API.Authorization;

/// <summary>
/// Evaluates an any-of permission requirement through the central effective-permission
/// resolver, exactly like <see cref="PermissionAuthorizationHandler"/>. JWT claims are not
/// trusted here, so a revoked grant stops working on the next request.
/// </summary>
public class AnyPermissionAuthorizationHandler : AuthorizationHandler<AnyPermissionRequirement>
{
    private readonly IEffectivePermissionService effectivePermissions;

    public AnyPermissionAuthorizationHandler(IEffectivePermissionService effectivePermissions)
    {
        this.effectivePermissions = effectivePermissions;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId) || userId == Guid.Empty)
        {
            return;
        }

        if (await effectivePermissions.HasAnyPermissionAsync(userId, requirement.Permissions))
        {
            context.Succeed(requirement);
        }
    }
}
