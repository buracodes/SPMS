using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configuration
{
    public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
    {
        public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
        {
            builder.HasKey(a => a.Id);

            builder.Property(a => a.EntityType)
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(a => a.EntityId)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(a => a.Action)
                .IsRequired();

            builder.Property(a => a.OldValues);

            builder.Property(a => a.NewValues);

            builder.Property(a => a.PerformedByName)
                .HasMaxLength(256);

            builder.Property(a => a.Reason)
                .HasMaxLength(1000);

            builder.Property(a => a.PerformedAt)
                .IsRequired();

            builder.HasIndex(a => new { a.EntityType, a.EntityId });
            builder.HasIndex(a => a.PerformedAt);
        }
    }
}
