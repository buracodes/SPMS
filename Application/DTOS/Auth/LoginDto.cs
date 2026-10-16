using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Auth
{
    public class LoginDto
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
    }
}