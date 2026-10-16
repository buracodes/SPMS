using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Constants
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string PowerUser = "Power User";
        public const string Executive = "Executive";
        public const string DataInputUser = "Data Input User";
        public const string ViewOnly = "View Only";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Admin,
            PowerUser,
            Executive,
            DataInputUser,
            ViewOnly
        };
    }
}