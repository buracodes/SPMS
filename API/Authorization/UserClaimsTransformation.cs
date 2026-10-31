using Application.Interface.IRepository;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace API.Authorization;

/// <summary>
/// Injects the principal's <em>current</em> division assignment into the request identity.
/// The JWT deliberately carries no organizational or HR claims: those change after sign-in, so
/// every request resolves them from the database instead of trusting a stale token.
/// </summary>
public sealed class UserClaimsTransformation : IClaimsTransformation
{
    public const string DivisionClaimType = "division";

    private readonly IUserRepository userRepository;

    public UserClaimsTransformation(IUserRepository userRepository)
    {
        this.userRepository = userRepository;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (principal.HasClaim(claim => claim.Type == DivisionClaimType))
        {
            return principal;
        }

        var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idText, out var userId) || userId == Guid.Empty)
        {
            return principal;
        }

        var divisionId = await userRepository.GetDivisionIdAsync(userId);
        if (divisionId is null)
        {
            return principal;
        }

        // Authentication identities can be read-only, so rebuild rather than mutating in place.
        var identity = new ClaimsIdentity(
            principal.Claims,
            principal.Identity.AuthenticationType,
            ClaimTypes.Name,
            ClaimTypes.Role);
        identity.AddClaim(new Claim(DivisionClaimType, divisionId.Value.ToString()));

        return new ClaimsPrincipal(identity);
    }
}
