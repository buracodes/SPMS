using Application.DTOS.Auth;
using Application.Interface.IRepository;
using DocumentFormat.OpenXml.Spreadsheet;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Text;

namespace Infrastructure.Persistence.Repositories
{
    public class UserRepository : GenericRepository<User>, IUserRepository
    {
        private readonly SpmsDbContext context;
        private readonly IConfiguration? configuration;

        public UserRepository(SpmsDbContext _context, IConfiguration? configuration = null) : base(_context)
        {
            this.context = _context;
            this.configuration = configuration;
        }

        public AdAuthenticationResult AuthenticateToAD(string userName, string pwd)
        {

            string _domain = "ecx.com";

            string LDAP_Path = "LDAP://ecx.com/DC=ecx,DC=com";

            if (string.IsNullOrEmpty(_domain) || string.IsNullOrEmpty(LDAP_Path))

                return AdAuthenticationResult.Failed;

            try
            {
                #region Authenticate using Directory Search
                using (System.DirectoryServices.DirectoryEntry entry = new(LDAP_Path, userName, pwd, AuthenticationTypes.Secure | AuthenticationTypes.Sealing | AuthenticationTypes.Signing))
                {
                    object obj = entry.NativeObject;

                    DirectorySearcher search = new DirectorySearcher(entry);
                    search.Filter = "(sAMAccountName=" + userName + ")";

                    search.PropertiesToLoad.Add("CN");

                    search.PropertiesToLoad.Add("distinguishedName");

                    SearchResultCollection results = search.FindAll();
                    if (results == null || results.Count != 1)
                    {
                        results?.Dispose();
                        return AdAuthenticationResult.Failed;
                    }

                    SearchResult result = results[0];
                    if (result == null)
                    {
                        results.Dispose();
                        return AdAuthenticationResult.Failed;
                    }

                    var commonName = result.Properties["CN"];
                    string? displayName = commonName.Count > 0 ? commonName[0]?.ToString() : null;

                    System.DirectoryServices.DirectoryEntry userADEntry = result.GetDirectoryEntry();
                    var authenticatedUser = new AdAuthenticationResult(
                        true,
                        userADEntry.Guid,
                        userName,
                        string.IsNullOrWhiteSpace(displayName) ? userName : displayName,
                        userName + "@ecx.com.et");

                    entry.Close();

                    return authenticatedUser;
                }

                #endregion

            }

            catch (Exception)

            {
                return AdAuthenticationResult.Failed;
            }
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<User?> GetByUsernameWithRolesAndPermissionsAsync(string username)
        {
            return await context.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<User?> GetByIdWithRolesAndPermissionsAsync(Guid id)
        {
            return await context.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<User?> GetByDivisionIdAsync(Guid divisionId)
        {
            return await context.Users
                .FirstOrDefaultAsync(u => u.DivisionId == divisionId);
        }

        public async Task<IReadOnlyList<User>> GetAllWithRolesAndDivisionAsync()
        {
            return await context.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.Division)
                .OrderBy(u => u.FullName)
                .ToListAsync();
        }

        public async Task<User?> ProvisionAdUserAsync(
            Guid userId,
            string username,
            string fullName,
            string? email,
            string? roleName = null,
            CancellationToken cancellationToken = default)
        {
            var role = string.IsNullOrWhiteSpace(roleName)
                ? null
                : await context.Roles.FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"The provisioning role '{roleName}' does not exist. Seed the role before provisioning.");

            var user = new User
            {
                Id = userId,
                Username = username,
                FullName = string.IsNullOrWhiteSpace(fullName) ? username : fullName,
                Email = email,
                IsActive = true
            };

            UserRole? membership = null;
            if (role != null)
            {
                membership = new UserRole { UserId = userId, RoleId = role.Id };
                user.UserRoles.Add(membership);
            }

            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);

            // The reload is no-tracking, so leaving the provisioning graph attached makes the caller's
            // Update() attach a second instance with the same key and throw. The role is reached through
            // UserRole.Role, so it has to be detached as well.
            context.Entry(user).State = EntityState.Detached;
            if (membership != null)
            {
                context.Entry(membership).State = EntityState.Detached;
            }
            if (role != null)
            {
                context.Entry(role).State = EntityState.Detached;
            }

            return await GetByIdWithRolesAndPermissionsAsync(userId);
        }

        public Task<Guid?> GetDivisionIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.DivisionId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Returns the local account for an HR employee, creating a shadow row keyed on the AD object
        /// GUID when the employee has never signed in. Access can be granted before first login; that
        /// sign-in resolves the same row by GUID, so no username or email matching is involved.
        /// </summary>
        public async Task<User> EnsureEmployeeAccountAsync(
            Guid userId,
            string fullName,
            string usernameHint,
            Guid actor,
            CancellationToken cancellationToken = default)
        {
            var existing = await context.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            var user = new User
            {
                Id = userId,
                Username = await BuildUniqueUsernameAsync(usernameHint, userId, cancellationToken),
                FullName = string.IsNullOrWhiteSpace(fullName) ? userId.ToString() : fullName,
                // Left empty on purpose: HR addresses are inconsistent, and the real address is
                // recorded from AD at the employee's first sign-in.
                Email = null,
                IsActive = true,
                Status = Domain.Enums.EntityStatus.Active,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            };

            await context.Users.AddAsync(user, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return user;
        }

        private async Task<string> BuildUniqueUsernameAsync(string hint, Guid userId, CancellationToken cancellationToken)
        {
            var baseName = string.IsNullOrWhiteSpace(hint) ? $"hr-{userId:N}" : hint.Trim();
            if (baseName.Length > 90)
            {
                baseName = baseName[..90];
            }

            // Soft-deleted rows still occupy the unfiltered unique index, so ignore the query filter.
            var taken = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Username == baseName, cancellationToken);
            if (!taken)
            {
                return baseName;
            }

            var unique = $"{baseName}-{userId.ToString("N")[..6]}";
            return unique.Length <= 100 ? unique : unique[..100];
        }
    }
}
