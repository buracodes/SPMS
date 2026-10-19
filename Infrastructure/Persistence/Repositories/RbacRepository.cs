using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Dynamic RBAC reads (ADR-011): the UserRight spine plus the join graphs the Access
    /// Control Management page and the centralized permission policy evaluate. All queries
    /// can opt into IgnoreQueryFilters because the page legitimately lists de-activated
    /// groups (soft-deleted roles) even though the global filter hides them from business reads.
    /// </summary>
    public class RbacRepository : GenericRepository<UserRight>, IRbacRepository
    {
        public RbacRepository(SpmsDbContext _context) : base(_context)
        {
        }

        public async Task<IReadOnlyList<User>> GetDirectoryUsersAsync(CancellationToken cancellationToken = default)
        {
            return await context.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.Division)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .OrderBy(u => u.FullName)
                .ToListAsync(cancellationToken);
        }

        public async Task<User?> GetUserAccessGraphAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await context.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.Division)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
                .Include(u => u.UserRights!)
                    .ThenInclude(urr => urr.Permission)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<Role>> GetAllRolesWithRightsAsync(CancellationToken cancellationToken = default)
        {
            return await context.Roles
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .Include(r => r.UserRoles)
                .ToListAsync(cancellationToken);
        }

        public async Task<Role?> GetRoleWithRightsAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            return await context.Roles.IgnoreQueryFilters()
                .AsSplitQuery()
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .Include(r => r.UserRoles)
                    .ThenInclude(ur => ur.User)
                .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        }

        public async Task<IReadOnlyList<UserRight>> GetUserRightsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await context.UserRights
                .AsNoTracking()
                .AsSplitQuery()
                .Include(urr => urr.Permission)
                .Include(urr => urr.User)
                .Where(urr => urr.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default)
        {
            return (await GetActiveAdministratorUserIdsInternalAsync(null, null, cancellationToken)).Count;
        }

        public async Task<IReadOnlyList<Guid>> GetActiveAdministratorUserIdsAsync(CancellationToken cancellationToken = default)
        {
            return await GetActiveAdministratorUserIdsInternalAsync(null, null, cancellationToken);
        }

        public async Task<int> CountActiveAdministratorsExcludingRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            return (await GetActiveAdministratorUserIdsInternalAsync(new[] { roleId }, null, cancellationToken)).Count;
        }

        private async Task<IReadOnlyList<Guid>> GetActiveAdministratorUserIdsInternalAsync(Guid[]? excludeRoleIds, Guid? excludeUserIdDirectRights, CancellationToken cancellationToken)
        {
            var users = await LoadActiveUsersWithAssignmentGraphAsync(cancellationToken);
            var grantedPermissionNamesByUser = ComputeEffectiveGrants(users, excludeRoleIds, excludeUserIdDirectRights);

            var adminIds = new List<Guid>();
            foreach (var user in users)
            {
                var names = grantedPermissionNamesByUser[user.Id];
                if (ManagementKeys.All(names.Contains))
                {
                    adminIds.Add(user.Id);
                }
            }
            return adminIds;
        }

        public async Task<IReadOnlyList<string>> GetRoleGrantedPermissionNamesAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            return await context.RolePermissions.IgnoreQueryFilters()
                .Where(rp => rp.RoleId == roleId && rp.Effect == Domain.Enums.PermissionEffect.Grant)
                .Select(rp => rp.Permission.Name)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// The management keys both of which (effective, deny-aware) define the administrator set.
        /// </summary>
        private static readonly string[] ManagementKeys =
        {
            Domain.Constants.Permissions.AdminUsersManage,
            Domain.Constants.Permissions.AdminRolesManage
        };

        /// <summary>The active administrator set = active users that effectively hold BOTH management keys (ADR-011).</summary>
        private async Task<IReadOnlyList<User>> LoadActiveUsersWithAssignmentGraphAsync(CancellationToken cancellationToken)
        {
            return await context.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
                .Include(u => u.UserRights!)
                    .ThenInclude(urr => urr.Permission)
                .Where(u => !u.IsDeleted && u.Status == Domain.Enums.EntityStatus.Active)
                .ToListAsync(cancellationToken);
        }

        private Dictionary<Guid, HashSet<string>> ComputeEffectiveGrants(
            IReadOnlyList<User> users,
            Guid[]? excludeRoleIds,
            Guid? excludeUserIdDirectRights)
        {
            var result = new Dictionary<Guid, HashSet<string>>();
            foreach (var user in users)
            {
                // Direct grants (unless excluded) and direct denies start the policy.
                var grants = new HashSet<string>();
                var denies = new HashSet<string>();
                foreach (var userRight in user.UserRights ?? new List<UserRight>())
                {
                    if (userRight.IsDeleted || userRight.Status != Domain.Enums.EntityStatus.Active)
                    {
                        continue;
                    }

                    if (userRight.Permission == null)
                    {
                        continue;
                    }

                    if (!ManagementKeys.Contains(userRight.Permission.Name))
                    {
                        continue;
                    }

                    if (userRight.Effect == Domain.Enums.PermissionEffect.Deny)
                    {
                        denies.Add(userRight.Permission.Name);
                    }
                    else if (excludeUserIdDirectRights == null || excludeUserIdDirectRights != user.Id)
                    {
                        grants.Add(userRight.Permission.Name);
                    }
                }

                foreach (var membership in user.UserRoles ?? new List<UserRole>())
                {
                    var role = membership.Role;
                    if (role == null || role.IsDeleted || role.Status != Domain.Enums.EntityStatus.Active ||
                        membership.IsDeleted || membership.Status != Domain.Enums.EntityStatus.Active)
                    {
                        continue;
                    }

                    var roleExcluded = excludeRoleIds != null && excludeRoleIds.Contains(role.Id);
                    foreach (var roleRight in role.RolePermissions ?? new List<RolePermission>())
                    {
                        var name = roleRight.Permission?.Name;
                        if (name == null || !ManagementKeys.Contains(name))
                        {
                            continue;
                        }

                        if (roleRight.Effect == Domain.Enums.PermissionEffect.Deny)
                        {
                            denies.Add(name);
                        }
                        else if (!roleExcluded)
                        {
                            grants.Add(name);
                        }
                    }
                }

                // Deny wins over grant under the central precedence policy (ADR-011).
                result[user.Id] = new HashSet<string>(grants.Where(g => !denies.Contains(g)));
            }
            return result;
        }
    }
}
