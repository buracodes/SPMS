using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class KraConfiguration : IEntityTypeConfiguration<Kra>
{
    public void Configure(EntityTypeBuilder<Kra> builder)
    {
        builder.Property(k => k.Name).HasMaxLength(200).IsRequired().UseCollation("Latin1_General_100_CI_AS_SC");
        builder.Property(k => k.Description).HasMaxLength(1000);
        builder.Property(k => k.Icon).HasMaxLength(50);
        builder.Property(k => k.UnitMeasure).HasMaxLength(50);
        builder.Property(k => k.Weight).HasPrecision(5, 2);
        builder.Property(k => k.IsActive).IsRequired();

        builder.HasOne(k => k.Scorecard)
            .WithMany(s => s.Kras)
            .HasForeignKey(k => k.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.Goal)
            .WithMany(g => g.Kras)
            .HasForeignKey(k => k.GoalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.StrategicTheme)
            .WithMany()
            .HasForeignKey(k => k.StrategicThemeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(k => new { k.ScorecardId, k.GoalId, k.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(k => k.StrategicThemeId);

        // Same periodicity guard as CK_Goals_Weight_Range: the CAST keeps the predicate
        // meaningful on SQLite (which stores decimal as TEXT) while staying valid on SQL Server.
        builder.ToTable(tb => tb.HasCheckConstraint("CK_Kras_Weight_Range", "CAST([Weight] AS REAL) > 0.0 AND CAST([Weight] AS REAL) <= 100.0"));
    }
}
