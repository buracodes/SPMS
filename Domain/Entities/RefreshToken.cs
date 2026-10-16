using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public required string TokenHash { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public string? RevokedByIp { get; set; }
        public string? CreatedByIp { get; set; }
        public string? ReasonRevoked { get; set; }
        public Guid? ReplacedByTokenId { get; set; }
        public User? User { get; set; }
    }
}