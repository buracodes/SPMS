using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Interface.IRepository;

/// <summary>
/// Read/write access for direct user rights (UserRight) plus the joins the Access Control
/// Management page and the centralized authorization policy need (dynamic RBAC, ADR-011).
/// The existing Role/Permission/UserRole/RolePermission spines are reused; this repository
/// only adds the UserRight spine and the aggregate reads on top of the same tables.
/// </summary>
public interface IRbacRepository
{
    /// <summary>Active users with their roles, division link and direct rights (employee directory).</summary>
    Task<IReadOnlyList<User>> GetDirectoryUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>One user with group memberships (roles), their rights and direct rights.</summary>
    Task<User?> GetUserAccessGraphAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Roles with their rights (role-permission rows and permission definitions).</summary>
    Task<IReadOnlyList<Role>> GetAllRolesWithRightsAsync(CancellationToken cancellationToken = default);

    /// <summary>One role with its rights and member count graph.</summary>
    Task<Role?> GetRoleWithRightsAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Direct rights of one user, with their permission rows.</summary>
    Task<IReadOnlyList<UserRight>> GetUserRightsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active users that effectively hold both <c>admin:users.manage</c> and
    /// <c>admin:roles.manage</c> (groups + direct rights, deny-aware) — the administrator set
    /// used by the last-administrator safeguard. A member right can only be changed away when
    /// at least one other member of this set is unaffected by the change.
    /// </summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default);

    /// <summary>The identities of that administrator set (see <see cref="CountActiveAdministratorsAsync"/>).</summary>
    Task<IReadOnlyList<Guid>> GetActiveAdministratorUserIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Same count, but computed as if the given role contributed no grants (while its denies
    /// stayed enforced) — the hypothetical used by group-right, membership and status guards.
    /// </summary>
    Task<int> CountActiveAdministratorsExcludingRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Names of permissions a role currently grants (effect Grant), for reference checks.</summary>
    Task<IReadOnlyList<string>> GetRoleGrantedPermissionNamesAsync(Guid roleId, CancellationToken cancellationToken = default);
}
