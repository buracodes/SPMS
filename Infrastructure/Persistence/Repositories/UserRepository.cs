using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Repositories
{
    public class UserRepository : GenericRepository<User>, IUserRepository
    {
        public UserRepository(SpmsDbContext _context) : base(_context)
        {
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
    }
}