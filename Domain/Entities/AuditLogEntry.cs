using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class AuditLogEntry
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public AuditAction Action { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public Guid? PerformedBy { get; set; }
        public string? PerformedByName { get; set; }
        public DateTime PerformedAt { get; set; }
        public string? Reason { get; set; }
    }
}