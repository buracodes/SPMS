using Application.Interface.IService;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace API.Authorization;

/// <summary>
/// Injects the principal's <em>current</em> division scope into the request identity.
/// The JWT deliberately carries no organizational or HR claims: those change after sign-in, so
/// every request resolves them from HR (read-only) and local explicit grants instead of trusting
/// a stale token. The HR primary division is emitted first, then any explicitly authorized
/// divisions; every value is validated against a local, active, synchronized division.
/// </summary>
public sealed class UserClaimsTransformation : IClaimsTransformation
{
    public const string DivisionClaimType = "division";

    // Marks an identity whose division claims already came from live state. Tokens issued before
    // the live scope resolver existed can still carry "division" claims, so the presence of a
    // division claim alone must never be treated as proof that the scope is current.
    private const string ScopeResolvedClaimType = "spms_division_scope";

    private readonly IEmployeeAccessResolver employeeAccess;

    public UserClaimsTransformation(IEmployeeAccessResolver employeeAccess)
    {
        this.employeeAccess = employeeAccess;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (principal.HasClaim(claim => claim.Type == ScopeResolvedClaimType))
        {
            return principal;
        }

        var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idText, out var userId) || userId == Guid.Empty)
        {
            return principal;
        }

        var divisionIds = (await employeeAccess.ResolveAsync(userId)).ScopedDivisionIds;

        // Authentication identities can be read-only, so rebuild rather than mutating in place.
        // Any legacy division claim is dropped: the live resolver is the only authority for scope.
        var identity = new ClaimsIdentity(
            principal.Claims.Where(claim => claim.Type != DivisionClaimType),
            principal.Identity.AuthenticationType,
            ClaimTypes.Name,
            ClaimTypes.Role);
        identity.AddClaim(new Claim(ScopeResolvedClaimType, "1"));
        foreach (var divisionId in divisionIds)
        {
            identity.AddClaim(new Claim(DivisionClaimType, divisionId.ToString()));
        }

        return new ClaimsPrincipal(identity);
    }
}
