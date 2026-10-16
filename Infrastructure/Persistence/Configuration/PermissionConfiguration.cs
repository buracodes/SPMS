using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configuration
{
    public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
    {
        public void Configure(EntityTypeBuilder<Permission> builder)
        {
            builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
            builder.HasIndex(p => p.Name).IsUnique();

            builder.Property(p => p.Module).HasMaxLength(100);
            builder.Property(p => p.Description).HasMaxLength(500);
        }
    }
}