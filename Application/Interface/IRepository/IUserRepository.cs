using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interface.IRepository
{
    public interface IUserRepository : IGenericRepository<User>
    {
        User AuthenticateToAD(string username, string pwd);
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByUsernameWithRolesAndPermissionsAsync(string username);
        Task<User?> GetByIdWithRolesAndPermissionsAsync(Guid id);
        Task<User?> GetByDivisionIdAsync(Guid divisionId);
        Task<IReadOnlyList<User>> GetAllWithRolesAndDivisionAsync();
    }
}