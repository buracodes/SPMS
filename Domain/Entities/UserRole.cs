using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class UserRole : CommonEntity
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
        public User User { get; set; } = null!;
        public Role Role { get; set; } = null!;
    }
}