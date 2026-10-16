using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class User : CommonEntity
    {
        public required string Username { get; set; }
        public string? Email { get; set; }
        public required string FullName { get; set; }
        public required string PasswordHash { get; set; }
        public bool IsActive { get; set; } = true;
        public int FailedLoginCount { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
        public DateTime? LastLoginAtUtc { get; set; }
        public Guid? DivisionId { get; set; }
        public Division? Division { get; set; }
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}