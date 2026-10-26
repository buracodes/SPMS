using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configuration
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
            builder.HasIndex(u => u.Username).IsUnique();

            builder.Property(u => u.Email).HasMaxLength(255);
            builder.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");

            builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();

            builder.HasOne(u => u.Division)
                .WithMany()
                .HasForeignKey(u => u.DivisionId)
                .OnDelete(DeleteBehavior.Restrict);

            // A division has many employees, so this index only supports lookups.
            builder.HasIndex(u => u.DivisionId);
        }
    }
}