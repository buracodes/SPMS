using Microsoft.AspNetCore.Authorization;

namespace API.Authorization;

/// <summary>
/// Satisfied when the principal effectively holds at least one of the listed permissions.
/// Read endpoints use this so a baseline division right and a broad corporate role right can
/// both reach the same action without duplicating the precedence rules in the controller.
/// </summary>
public class AnyPermissionRequirement : IAuthorizationRequirement
{
    public AnyPermissionRequirement(params string[] permissions)
    {
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>Stable policy names for the any-of requirements used by controllers.</summary>
public static class AnyPermissionPolicyNames
{
    public const string DivisionRead = "division:read-any";
    public const string InitiativeRead = "initiative:read-any";
}
