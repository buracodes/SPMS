using API.Authorization;
using Application.Interface.IService;
using Application.Tests.TestSupport;
using System.Security.Claims;

namespace Application.Tests.Auth;

/// <summary>
/// The request identity must always carry the live division scope. A token issued before the
/// live resolver existed can still hold a "division" claim, and that stale claim must not
/// survive a request made after the scope was revoked.
/// </summary>
public class UserClaimsTransformationTests
{
    private static ClaimsPrincipal SignedIn(Guid userId, params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(extra);

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static Guid[] DivisionClaims(ClaimsPrincipal principal)
        => principal.FindAll(UserClaimsTransformation.DivisionClaimType)
            .Select(claim => Guid.Parse(claim.Value))
            .ToArray();

    [Fact]
    public async Task Legacy_division_claim_is_replaced_by_the_live_scope()
    {
        var userId = Guid.NewGuid();
        var liveDivision = Guid.NewGuid();
        var staleDivision = Guid.NewGuid();
        var transformation = new UserClaimsTransformation(
            TestEmployeeAccessResolver.ActiveInDivision(liveDivision));

        var transformed = await transformation.TransformAsync(
            SignedIn(userId, new Claim(UserClaimsTransformation.DivisionClaimType, staleDivision.ToString())));

        Assert.Equal(new[] { liveDivision }, DivisionClaims(transformed));
    }

    [Fact]
    public async Task Revoked_division_scope_removes_every_division_claim_from_an_old_token()
    {
        var userId = Guid.NewGuid();
        var staleDivision = Guid.NewGuid();
        var transformation = new UserClaimsTransformation(
            new TestEmployeeAccessResolver { Snapshot = EmployeeAccessSnapshot.None });

        var transformed = await transformation.TransformAsync(
            SignedIn(userId, new Claim(UserClaimsTransformation.DivisionClaimType, staleDivision.ToString())));

        Assert.Empty(DivisionClaims(transformed));
    }

    [Fact]
    public async Task An_already_resolved_identity_is_not_transformed_again()
    {
        var userId = Guid.NewGuid();
        var liveDivision = Guid.NewGuid();
        var transformation = new UserClaimsTransformation(
            TestEmployeeAccessResolver.ActiveInDivision(liveDivision));

        var first = await transformation.TransformAsync(SignedIn(userId));
        var second = await transformation.TransformAsync(first);

        Assert.Same(first, second);
        Assert.Equal(new[] { liveDivision }, DivisionClaims(second));
    }

    [Fact]
    public async Task Anonymous_identity_is_left_alone()
    {
        var transformation = new UserClaimsTransformation(
            TestEmployeeAccessResolver.ActiveInDivision(Guid.NewGuid()));
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Same(anonymous, await transformation.TransformAsync(anonymous));
    }
}
