using Application.Interface.IService;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace API.Authorization
{
    /// <summary>
    /// Centralized capability enforcement (ADR-011). The required permission is evaluated
    /// live through <see cref="IEffectivePermissionService"/> on every request — JWT claims
    /// are deliberately NOT consulted here, because a claim minted before an assignment
    /// was revoked could resurrect a permission until the token was refreshed.
    /// </summary>
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IEffectivePermissionService effectivePermissions;

        public PermissionAuthorizationHandler(IEffectivePermissionService effectivePermissions)
        {
            this.effectivePermissions = effectivePermissions;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var userId = ResolveUserId(context.User);
            if (userId == null || userId == Guid.Empty)
            {
                return;
            }

            if (await effectivePermissions.HasPermissionAsync(userId.Value, requirement.Permission))
            {
                context.Succeed(requirement);
            }
        }

        private static Guid? ResolveUserId(ClaimsPrincipal user)
        {
            var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
