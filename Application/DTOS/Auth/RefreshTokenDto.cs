using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Auth
{
    public class RefreshTokenDto
    {
        public required string RefreshToken { get; set; }
    }
}