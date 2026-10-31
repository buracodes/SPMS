using Application.DTOS.Auth;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IRepository
{
    public interface IUserRepository : IGenericRepository<User>
    {
        AdAuthenticationResult AuthenticateToAD(string username, string pwd);
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByUsernameWithRolesAndPermissionsAsync(string username);
        Task<User?> GetByIdWithRolesAndPermissionsAsync(Guid id);
        Task<User?> GetByDivisionIdAsync(Guid divisionId);
        Task<IReadOnlyList<User>> GetAllWithRolesAndDivisionAsync();

        /// <summary>
        /// Creates the local account for an identity that authenticated against AD but has no
        /// SPMS row yet. No role is assigned by default: access is default-deny until an
        /// administrator grants a group membership or direct right. A role name is supplied only
        /// for explicitly configured bootstrap administrators. Returns the reloaded user.
        /// The id is the AD object GUID, which is what later logins are matched on.
        /// </summary>
        Task<User?> ProvisionAdUserAsync(
            Guid userId,
            string username,
            string fullName,
            string? email,
            string? roleName = null,
            CancellationToken cancellationToken = default);

        /// <summary>Current division assignment, read from the database rather than a token claim.</summary>
        Task<Guid?> GetDivisionIdAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the local account for an HR employee, creating a shadow row when the employee
        /// has never signed in. Access can therefore be granted before first login; that sign-in
        /// matches the shadow row by the same AD object GUID.
        /// </summary>
        Task<User> EnsureEmployeeAccountAsync(
            Guid userId,
            string fullName,
            string usernameHint,
            Guid actor,
            CancellationToken cancellationToken = default);
    }
}