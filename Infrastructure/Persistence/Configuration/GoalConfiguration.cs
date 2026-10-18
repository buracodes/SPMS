using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired().UseCollation("Latin1_General_100_CI_AS_SC");
        builder.Property(g => g.Description).HasMaxLength(1000);
        builder.Property(g => g.Weight).HasPrecision(5, 2);
        builder.Property(g => g.IsActive).IsRequired();

        // FR-302: a single goal weight is a percentage - strictly greater than 0 and at
        // most 100. Row-level DB backstop behind the app validators so API writers
        // bypassing MediatR (bulk tools, manual scripts) cannot persist 0/negative/>100.
        // The per-scorecard SUM cap stays application-side (ADR-009) because SQL Server
        // CHECK constraints cannot span rows.
        builder.ToTable(tb => tb.HasCheckConstraint("CK_Goals_Weight_Range", "CAST([Weight] AS REAL) > 0.0 AND CAST([Weight] AS REAL) <= 100.0"));

        builder.HasOne(g => g.Scorecard)
            .WithMany(s => s.Goals)
            .HasForeignKey(g => g.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => new { g.ScorecardId, g.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
