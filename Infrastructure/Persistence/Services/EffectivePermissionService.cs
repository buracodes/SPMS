using Application.CQRS.AccessControl;
using Application.Interface.IService;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Services;

/// <summary>
/// Centralized permission-policy evaluation (ADR-011). Reads the live authorization state
/// (capability assignment tables) on every evaluation so administrative changes take
/// effect immediately; caching is intentionally avoided because the identity set is small
/// and each check is a request-bounded SQL read — <see cref="IEffectivePermissionService.InvalidateAllAsync"/>
/// is kept as the seam for a future cache. The only cached input is the opt-in HR position
/// lookup, which sits behind a short TTL inside <see cref="HrPositionPermissionProvider"/>.
///
/// Precedence, per right:
///   1. unauthenticated or deactivated identity          → default deny
///   2. explicit user-level deny                          → deny  (direct assignment)
///   3. explicit group-level deny (any of user's groups)  → deny  (group assignment)
///   4. direct user-level grant                           → grant (direct assignment)
///   5. group-level grant                                 → grant (group assignment)
///   6. optional HR position-level grant                  → grant (position assignment)
///   7. no applicable grant                               → default deny
/// </summary>
public class EffectivePermissionService : IEffectivePermissionService
{

    private const string ResultGranted = "Granted";
    private const string ResultDenied = "Denied";

    private readonly SpmsDbContext context;
    private readonly IPositionPermissionProvider positionPermissions;

    public EffectivePermissionService(SpmsDbContext context, IPositionPermissionProvider positionPermissions)
    {
        this.context = context;
        this.positionPermissions = positionPermissions;
    }

    public void InvalidateAllAsync()
    {
        // Live evaluation per request: nothing to invalidate today.
    }

    /// <summary>One evaluated right: result, where it came from, and whether any deny applied.</summary>
    private sealed record Decision(AccessResult Result, AccessSource Source, bool HasDeny, string Explanation);

    public async Task<IReadOnlySet<string>> GetEffectiveGrantNamesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new HashSet<string>();
        }

        var state = await LoadStateAsync(userId, cancellationToken);
        if (!IsIdentityActive(state.User))
        {
            return new HashSet<string>();
        }

        var positionGrants = await positionPermissions.GetGrantedPermissionsAsync(state.User!.Id, cancellationToken);
        var knownNames = CollectPermissionNames(state);
        knownNames.UnionWith(positionGrants);
        return new HashSet<string>(
            knownNames.Where(name => Evaluate(name, state, positionGrants)?.Result == AccessResult.Granted));
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var grants = await GetEffectiveGrantNamesAsync(userId, cancellationToken);
        return grants.Contains(permission);
    }

    /// <summary>Builds the full explanation table for the Effective Access tab — one entry per catalog right.</summary>
    public async Task<EffectiveAccessResultDto> EvaluateDetailedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(userId, cancellationToken);
        var catalog = await context.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var byName = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in catalog)
        {
            byName[permission.Name] = permission;
        }

        var identityActive = IsIdentityActive(state.User);
        var positionGrants = identityActive
            ? await positionPermissions.GetGrantedPermissionsAsync(state.User!.Id, cancellationToken)
            : (IReadOnlySet<string>)new HashSet<string>();
        var groupNames = state.Memberships
            .Select(m => m.Role?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToArray();

        var entries = new List<EffectiveAccessEntryDto>(catalog.Count);
        foreach (var permission in catalog)
        {
            entries.Add(identityActive
                ? Describe(permission, Evaluate(permission.Name, state, positionGrants))
                : Describe(permission, null));
        }

        return new EffectiveAccessResultDto(
            state.User?.Id ?? userId,
            state.User?.Username ?? string.Empty,
            state.User?.FullName ?? string.Empty,
            identityActive,
            groupNames,
            entries.ToArray());
    }

    private sealed record AuthorizationState(
        User? User,
        List<(UserRole Membership, Role Role, List<(RolePermission Right, Permission Permission)>)> Memberships,
        List<(UserRight Right, Permission Permission)> UserRights);

    private async Task<AuthorizationState> LoadStateAsync(Guid userId, CancellationToken ct)
    {
        var user = await context.Users
            .AsNoTracking()
            .Include(u => u.Division)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        var membershipRows = await context.UserRoles
            .AsNoTracking()
            .AsSplitQuery()
            .Where(ur => ur.UserId == userId)
            .Select(ur => new
            {
                Membership = ur,
                Role = ur.Role,
                Rights = ur.Role.RolePermissions
                    .Select(rp => new { rp, rp.Permission })
                    .ToList()
            })
            .ToListAsync(ct);

        var memberships = membershipRows
            .Select(row => (row.Membership, row.Role,
                row.Rights.Select(x => (x.rp, x.Permission)).ToList()))
            .ToList();

        var rightRows = await context.UserRights
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => new { ur, ur.Permission })
            .ToListAsync(ct);

        var userRights = rightRows.Select(x => (x.ur, x.Permission)).ToList();
        return new AuthorizationState(user, memberships, userRights);
    }

    private static bool IsIdentityActive(User? user) =>
        user != null && !user.IsDeleted && user.Status == EntityStatus.Active && user.IsActive;

    /// <summary>The single precedence evaluation, applied to every consumer.</summary>
    private static Decision? Evaluate(
        string permissionName,
        AuthorizationState state,
        IReadOnlySet<string> positionGrants)
    {
        bool directDeny = false, groupDeny = false, directGrant = false, groupGrant = false;

        foreach (var (right, permission) in state.UserRights)
        {
            if (!IsUsable(right.IsDeleted, right.Status) || permission == null ||
                !Matches(permission.Name, permissionName))
            {
                continue;
            }

            if (right.Effect == PermissionEffect.Deny)
            {
                directDeny = true;
            }
            else
            {
                directGrant = true;
            }
        }

        if (directDeny)
        {
            return new Decision(AccessResult.Denied, AccessSource.DirectUserAssignment, true,
                "An explicit user-level deny overrides all grants under the configured authorization policy.");
        }

        foreach (var (membership, role, rights) in state.Memberships)
        {
            if (!IsUsable(membership.IsDeleted, membership.Status) ||
                role == null || !IsRoleUsable(role))
            {
                continue;
            }

            foreach (var (right, permission) in rights)
            {
                if (permission == null || !Matches(permission.Name, permissionName))
                {
                    continue;
                }

                if (right.Effect == PermissionEffect.Deny)
                {
                    groupDeny = true;
                }
                else
                {
                    groupGrant = true;
                }
            }
        }

        if (groupDeny)
        {
            return new Decision(AccessResult.Denied, AccessSource.GroupAssignment, true,
                "An explicit group-level deny overrides all grants for the group's members under the configured authorization policy.");
        }

        if (directGrant)
        {
            return new Decision(AccessResult.Granted, AccessSource.DirectUserAssignment, false,
                "Granted through a direct user assignment.");
        }

        if (groupGrant)
        {
            return new Decision(AccessResult.Granted, AccessSource.GroupAssignment, false,
                "Granted through one of the user's access groups.");
        }

        if (positionGrants.Contains(permissionName))
        {
            return new Decision(AccessResult.Granted, AccessSource.PositionAssignment, false,
                "Granted through the user's HR position level mapping.");
        }

        return null; // default deny
    }

    private static EffectiveAccessEntryDto Describe(Permission permission, Decision? decision) => decision switch
    {
        null => new EffectiveAccessEntryDto(permission.Id, permission.Name, permission.Module, permission.Description,
            ResultDenied, AccessSourceName(AccessSource.None), false,
            "No applicable grant exists for this right, so access is denied by default."),
        _ => new EffectiveAccessEntryDto(permission.Id, permission.Name, permission.Module, permission.Description,
            decision.Result == AccessResult.Granted ? ResultGranted : ResultDenied,
            AccessSourceName(decision.Source), decision.HasDeny, decision.Explanation)
    };

    private static string AccessSourceName(AccessSource source) => source switch
    {
        AccessSource.DirectUserAssignment => "Direct User Assignment",
        AccessSource.GroupAssignment => "Group Assignment",
        AccessSource.PositionAssignment => "Position Assignment",
        _ => "Default Deny"
    };

    private static HashSet<string> CollectPermissionNames(AuthorizationState state)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (right, permission) in state.UserRights)
        {
            if (permission != null)
            {
                names.Add(permission.Name);
            }
        }

        foreach (var (_, role, rights) in state.Memberships)
        {
            if (role == null || !IsRoleUsable(role))
            {
                continue;
            }

            foreach (var (_, permission) in rights)
            {
                if (permission != null)
                {
                    names.Add(permission.Name);
                }
            }
        }

        return names;
    }

    private static bool Matches(string candidateName, string permissionName) =>
        string.Equals(candidateName, permissionName, StringComparison.OrdinalIgnoreCase);

    private static bool IsUsable(bool isDeleted, EntityStatus status) =>
        !isDeleted && status == EntityStatus.Active;

    private static bool IsRoleUsable(Role role) =>
        !role.IsDeleted && role.Status == EntityStatus.Active;
}
