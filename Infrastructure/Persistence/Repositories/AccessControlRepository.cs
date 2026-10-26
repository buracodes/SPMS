using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Repositories
{
    public class AccessControlRepository : GenericRepository<Group>, IAccessControl
    {
        public AccessControlRepository(SpmsDbContext context) : base(context)
        {
            
        }
    }
}
