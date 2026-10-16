using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Auth
{
    public class TokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public AuthUserDto User { get; set; } = new();
    }
}