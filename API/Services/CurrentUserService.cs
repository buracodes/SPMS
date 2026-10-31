using Application.Interface.IService;
using Domain.Constants;
using System.Security.Claims;

namespace API.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            this.httpContextAccessor = httpContextAccessor;
        }

        public Guid? UserId
        {
            get
            {
                var value = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                return Guid.TryParse(value, out var id) ? id : null;
            }
        }

        public string? UserName => httpContextAccessor.HttpContext?.User?.Identity?.Name;

        /// <summary>
        /// Current division assignment. Tokens no longer carry it; <see cref="UserClaimsTransformation"/>
        /// adds it from the database on every request so a transfer takes effect immediately.
        /// </summary>
        public Guid? DivisionId
        {
            get
            {
                var value = httpContextAccessor.HttpContext?.User?.FindFirstValue("division");
                return Guid.TryParse(value, out var id) ? id : null;
            }
        }

        public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

        public IReadOnlyList<string> Permissions
        {
            get
            {
                var claims = httpContextAccessor.HttpContext?.User?
                    .FindAll(Domain.Constants.Permissions.PermissionClaimType)
                    .Select(c => c.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .ToArray();

                return claims ?? Array.Empty<string>();
            }
        }

        public IReadOnlyList<string> Roles
        {
            get
            {
                var claims = httpContextAccessor.HttpContext?.User?
                    .FindAll(System.Security.Claims.ClaimTypes.Role)
                    .Select(c => c.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return claims ?? Array.Empty<string>();
            }
        }
    }
}
