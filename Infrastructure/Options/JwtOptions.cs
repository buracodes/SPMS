using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Options
{
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = "SPMS";
        public string Audience { get; set; } = "SPMS";
        public string Secret { get; set; } = string.Empty;
        public int AccessTokenExpiryMinutes { get; set; } = 30;
    }
}
