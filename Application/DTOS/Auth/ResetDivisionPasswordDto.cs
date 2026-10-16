using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Auth
{
    public class ResetDivisionPasswordDto
    {
        public required Guid DivisionId { get; set; }
        public required string NewPassword { get; set; }
        public required string ConfirmPassword { get; set; }
    }
}
