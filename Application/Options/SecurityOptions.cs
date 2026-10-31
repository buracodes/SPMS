using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Options
{
    public class SecurityOptions
    {
        public const string SectionName = "Security";

        public int RefreshTokenExpiryDays { get; set; } = 7;

        /// <summary>Create a local SPMS account on the first successful AD login of an unknown identity. No role is assigned.</summary>
        public bool AutoProvisionAdUsers { get; set; } = true;

        /// <summary>
        /// AD usernames that receive <c>Admin</c> instead of no role when they are first
        /// provisioned. Authentication is AD-only, so nothing is seeded locally and this list is the
        /// only way a new database gets its first administrator. Everyone else starts default-deny.
        /// </summary>
        public string[] BootstrapAdminUsernames { get; set; } = Array.Empty<string>();
    }
}