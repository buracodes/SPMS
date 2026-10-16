using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class KraScoreSnapshotConfiguration : IEntityTypeConfiguration<KraScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<KraScoreSnapshot> builder)
    {
        builder.Property(s => s.Granularity).IsRequired();
        builder.Property(s => s.Score).HasPrecision(18, 4);
        builder.Property(s => s.CalculatedAt).IsRequired();

        builder.HasOne(s => s.Scorecard)
            .WithMany()
            .HasForeignKey(s => s.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Kra)
            .WithMany()
            .HasForeignKey(s => s.KraId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.FiscalPeriod)
            .WithMany()
            .HasForeignKey(s => s.FiscalPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.KraId, s.FiscalPeriodId, s.Granularity, s.CalculatedAt }).IsUnique();
        builder.HasIndex(s => s.ScorecardId);
    }
}
