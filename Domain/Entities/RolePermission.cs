using Domain.Common;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class RolePermission : CommonEntity
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }

        /// <summary>
        /// The explicit effect of this group-level assignment. Legacy rows (seeded before
        /// dynamic RBAC) carry Grant. A Deny removes the right from every member of the
        /// group even if another group or a direct user-right would grant it.
        /// </summary>
        public PermissionEffect Effect { get; set; } = PermissionEffect.Grant;

        public Role Role { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
    }
}