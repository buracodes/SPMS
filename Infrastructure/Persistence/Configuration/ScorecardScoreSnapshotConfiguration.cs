using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class ScorecardScoreSnapshotConfiguration : IEntityTypeConfiguration<ScorecardScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<ScorecardScoreSnapshot> builder)
    {
        builder.Property(s => s.Granularity).IsRequired();
        builder.Property(s => s.Score).HasPrecision(18, 4);
        builder.Property(s => s.CalculatedAt).IsRequired();

        builder.HasOne(s => s.Scorecard)
            .WithMany()
            .HasForeignKey(s => s.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.FiscalPeriod)
            .WithMany()
            .HasForeignKey(s => s.FiscalPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.ScorecardId, s.FiscalPeriodId, s.Granularity, s.CalculatedAt }).IsUnique();
    }
}
