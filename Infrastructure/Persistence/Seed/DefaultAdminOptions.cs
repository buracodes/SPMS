using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Seed
{
    public class DefaultAdminOptions
    {
        public const string SectionName = "DefaultAdmin";

        public string Username { get; set; } = "admin";
        public string FullName { get; set; } = "System Administrator";
        public string? Email { get; set; }
        public string Password { get; set; } = "admin";
    }
}