using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    /// <summary>
    /// Local identity row for an Active Directory account. Authentication, failed-attempt counting
    /// and lockout are all handled by AD; SPMS stores only the authorization data (group and direct
    /// right assignments). <see cref="CommonEntity.Id"/> is the AD object GUID, which is also HR's
    /// <c>Employee.EmpId</c>, so a row can be pre-created before the employee's first sign-in.
    /// </summary>
    public class User : CommonEntity
    {
        public required string Username { get; set; }
        public string? Email { get; set; }
        public required string FullName { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? DivisionId { get; set; }
        public Division? Division { get; set; }

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

        /// <summary>Direct (user-level) right assignments — grant or deny (dynamic RBAC, ADR-011).</summary>
        public ICollection<UserRight> UserRights { get; set; } = new List<UserRight>();
    }
}