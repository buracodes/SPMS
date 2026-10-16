using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Options
{
    public class SecurityOptions
    {
        public const string SectionName = "Security";

        public int MaxFailedAttempts { get; set; } = 3;
        public int LockoutMinutes { get; set; } = 15;
        public int RefreshTokenExpiryDays { get; set; } = 7;
    }
}