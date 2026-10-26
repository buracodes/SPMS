using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class KpiConfiguration : IEntityTypeConfiguration<Kpi>
{
    public void Configure(EntityTypeBuilder<Kpi> builder)
    {
        builder.Property(k => k.Name).HasMaxLength(200).IsRequired().UseCollation("Latin1_General_100_CI_AS_SC");
        builder.Property(k => k.Description).HasMaxLength(1000);
        builder.Property(k => k.Unit).HasMaxLength(50);
        builder.Property(k => k.Formula).HasMaxLength(500);

        builder.Property(k => k.Weight).HasPrecision(5, 2);
        builder.Property(k => k.Baseline).HasPrecision(18, 4);
        builder.Property(k => k.MinValue).HasPrecision(18, 4);
        builder.Property(k => k.MaxValue).HasPrecision(18, 4);
        builder.Property(k => k.ToleranceBandPercent).HasPrecision(5, 2);

        builder.Property(k => k.OptimizationType).IsRequired();
        builder.Property(k => k.DataSource).IsRequired();
        builder.Property(k => k.KpiType).IsRequired();
        builder.Property(k => k.IsCumulative).IsRequired();
        builder.Property(k => k.IsActive).IsRequired();

        builder.HasOne(k => k.Scorecard)
            .WithMany(s => s.Kpis)
            .HasForeignKey(k => k.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.Kra)
            .WithMany(kra => kra.Kpis)
            .HasForeignKey(k => k.KraId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.DivisionGoal)
            .WithMany(dg => dg.Kpis)
            .HasForeignKey(k => k.DivisionGoalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.Owner)
            .WithMany()
            .HasForeignKey(k => k.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.DivisionOwner)
            .WithMany()
            .HasForeignKey(k => k.DivisionOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(k => k.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.ParentKpi)
            .WithMany(p => p.ChildKpis)
            .HasForeignKey(k => k.ParentKpiId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(k => new { k.ScorecardId, k.KraId, k.Name })
            .IsUnique()
            .HasFilter("[KraId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(k => new { k.ScorecardId, k.DivisionGoalId, k.Name })
            .IsUnique()
            .HasFilter("[DivisionGoalId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(k => k.KraId);
        builder.HasIndex(k => k.DivisionGoalId);
        builder.HasIndex(k => k.ParentKpiId);
        builder.HasIndex(k => k.OwnerId);
        builder.HasIndex(k => k.DivisionOwnerId);

        builder.ToTable(k =>
        {
            // Layered with the handler/validator rules: exactly one parent (a KRA or a
            // division goal), and the weight must live in (0, 100] so a zero-weight KPI
            // can never silently join or skew a roll-up.
            k.HasCheckConstraint(
                "CK_Kpis_KraOrDivisionGoal",
                "([KraId] IS NOT NULL AND [DivisionGoalId] IS NULL) OR ([KraId] IS NULL AND [DivisionGoalId] IS NOT NULL)");
            k.HasCheckConstraint(
                "CK_Kpis_Weight_Range",
                "CAST([Weight] AS REAL) > 0.0 AND CAST([Weight] AS REAL) <= 100.0");
            // A KPI is owned by at most one account type. Both columns stay nullable so
            // legacy KPIs with no owner remain valid.
            k.HasCheckConstraint(
                "CK_Kpis_Owner_AtMostOne",
                "NOT ([OwnerId] IS NOT NULL AND [DivisionOwnerId] IS NOT NULL)");
        });
    }
}
