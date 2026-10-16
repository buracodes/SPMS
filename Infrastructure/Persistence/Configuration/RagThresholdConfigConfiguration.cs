using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class RagThresholdConfigConfiguration : IEntityTypeConfiguration<RagThresholdConfig>
{
    public void Configure(EntityTypeBuilder<RagThresholdConfig> builder)
    {
        builder.Property(r => r.ScopeKey).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Band).IsRequired();
        builder.Property(r => r.MinScore).HasPrecision(5, 2);
        builder.Property(r => r.MaxScore).HasPrecision(5, 2);
        builder.Property(r => r.Color).HasMaxLength(20).IsRequired();

        builder.HasIndex(r => new { r.ScopeKey, r.Band }).IsUnique();
    }
}
