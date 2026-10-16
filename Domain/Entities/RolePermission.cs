using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class RolePermission : CommonEntity
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }
        public Role Role { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
    }
}