using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class KpiTargetConfiguration : IEntityTypeConfiguration<KpiTarget>
{
    public void Configure(EntityTypeBuilder<KpiTarget> builder)
    {
        builder.Property(t => t.TargetValue).HasPrecision(18, 4);
        builder.Property(t => t.Granularity).IsRequired();

        builder.HasOne(t => t.Kpi)
            .WithMany()
            .HasForeignKey(t => t.KpiId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.FiscalPeriod)
            .WithMany()
            .HasForeignKey(t => t.FiscalPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique only among active rows: a deactivated (soft-deleted) stale combination
        // must be re-creatable later without clashing with its historical record.
        builder.HasIndex(t => new { t.KpiId, t.FiscalPeriodId, t.Granularity })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}