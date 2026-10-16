using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ValueJson).IsRequired();
        builder.Property(s => s.Category).IsRequired();

        builder.HasIndex(s => s.Key).IsUnique();
    }
}
