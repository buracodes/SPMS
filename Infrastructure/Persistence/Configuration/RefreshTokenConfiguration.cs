using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configuration
{
    public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.Property(rt => rt.TokenHash).HasMaxLength(256).IsRequired();
            builder.HasIndex(rt => rt.TokenHash).IsUnique();

            builder.Property(rt => rt.RevokedByIp).HasMaxLength(64);
            builder.Property(rt => rt.CreatedByIp).HasMaxLength(64);
            builder.Property(rt => rt.ReasonRevoked).HasMaxLength(500);

            builder.HasIndex(rt => rt.UserId);
        }
    }
}