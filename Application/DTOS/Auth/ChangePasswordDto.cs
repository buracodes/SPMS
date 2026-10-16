using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Auth
{
    public class ChangePasswordDto
    {
        public required string CurrentPassword { get; set; }
        public required string NewPassword { get; set; }
        public required string ConfirmPassword { get; set; }
    }
}
