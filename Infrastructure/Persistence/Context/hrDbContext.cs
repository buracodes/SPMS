using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Context
{
    public class hrDbContext : DbContext
    {
        public hrDbContext(DbContextOptions<hrDbContext> options) : base(options)
        {
            
        }
    }
}
