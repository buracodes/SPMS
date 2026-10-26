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

        public User AuthenticateToAD(string userName, string pwd)
        {

            string _domain = "ecx.com";

            string LDAP_Path = "LDAP://ecx.com/DC=ecx,DC=com";



            //string adAdminUser = "APPSA";

            //string adAdminPass = "09Solecx";

            User authenticatedUser;

            if (string.IsNullOrEmpty(_domain) || string.IsNullOrEmpty(LDAP_Path))

                return new User
                {
                    IsAuthenticated = false,
                    Username = string.Empty,
                    FullName = string.Empty,
                    PasswordHash = string.Empty,
                };

            string domainAndUsername = _domain + "\\" + userName;

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
                    if (results == null || results.Count == 0)
                    {
                        return new User
                        {
                            IsAuthenticated = false,
                            Username = string.Empty,
                            FullName = string.Empty,
                            PasswordHash = string.Empty,
                        };
                    }
                    if (results.Count > 1)
                    {
                        results.Dispose();
                        return new User
                        {
                            IsAuthenticated = false,
                            Username = string.Empty,
                            FullName = string.Empty,
                            PasswordHash = string.Empty,
                        };
                    }
                    SearchResult result = results[0];
                    if (result != null)
                    {
                        string distinguishedName = result.Properties["distinguishedName"][0].ToString();
                        System.DirectoryServices.DirectoryEntry userADEntry = result.GetDirectoryEntry();
                        authenticatedUser = new User
                        {
                            IsAuthenticated = true,
                            Id = userADEntry.Guid,
                            FullName = userADEntry.Username,
                            Email = userADEntry.Username + "@ecx.com.et",
                            Username = userADEntry.Properties["CN"][0].ToString(),
                            PasswordHash = string.Empty

                        };
                    }

                    else
                    {

                        return new User
                        {
                            IsAuthenticated = false,
                            Username = string.Empty,
                            FullName = string.Empty,
                            PasswordHash = string.Empty,
                        };

                    }

                    entry.Close();

                    return authenticatedUser;

                }

                #endregion

            }

            catch (Exception ex)

            {

                return new User
                {
                    IsAuthenticated = false,
                    Username = string.Empty,
                    FullName = string.Empty,
                    PasswordHash = string.Empty,
                };

            }
        }

        private static User FailedLogin(string username) => new()
        {
            IsAuthenticated = false,
            Username = username,
            FullName = string.Empty,
            PasswordHash = string.Empty
        };

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
    }
}
