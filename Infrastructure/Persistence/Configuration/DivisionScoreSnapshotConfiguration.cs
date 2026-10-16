using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class DivisionScoreSnapshotConfiguration : IEntityTypeConfiguration<DivisionScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<DivisionScoreSnapshot> builder)
    {
        builder.Property(s => s.Score).HasPrecision(18, 4);

        builder.HasOne(s => s.Scorecard)
            .WithMany()
            .HasForeignKey(s => s.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Division)
            .WithMany()
            .HasForeignKey(s => s.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.DivisionGoal)
            .WithMany()
            .HasForeignKey(s => s.DivisionGoalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.FiscalPeriod)
            .WithMany()
            .HasForeignKey(s => s.FiscalPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.ScorecardId);
        builder.HasIndex(s => s.DivisionId);
        builder.HasIndex(s => new { s.DivisionGoalId, s.FiscalPeriodId, s.Granularity, s.CalculatedAt });
    }
}
