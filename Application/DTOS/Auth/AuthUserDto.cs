using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Auth
{
    public class AuthUserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public Guid? DivisionId { get; set; }

        /// <summary>Every division this principal is authorized for; the primary division is first.</summary>
        public IReadOnlyList<Guid> DivisionIds { get; set; } = Array.Empty<Guid>();

        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    }
}