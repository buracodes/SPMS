using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class KpiActualValueConfiguration : IEntityTypeConfiguration<KpiActualValue>
{
    public void Configure(EntityTypeBuilder<KpiActualValue> builder)
    {
        builder.Property(a => a.Value).HasPrecision(18, 4);
        builder.Property(a => a.DataSource).IsRequired();
        builder.Property(a => a.AsOfDate).IsRequired();
        builder.Property(a => a.Note).HasMaxLength(1000);
        builder.Property(a => a.IsCurrent).IsRequired();

        builder.HasOne(a => a.Kpi)
            .WithMany()
            .HasForeignKey(a => a.KpiId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.FiscalPeriod)
            .WithMany()
            .HasForeignKey(a => a.FiscalPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Recorder)
            .WithMany()
            .HasForeignKey(a => a.RecordedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-reference: the new revision points at the previous revision it supersedes.
        // RESTRICT ensures the referenced revision cannot be physically deleted.
        builder.HasOne<KpiActualValue>()
            .WithMany()
            .HasForeignKey(a => a.SupersedesActualId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.KpiId, a.FiscalPeriodId, a.DataSource, a.AsOfDate });

        // One effective actual per KPI + fiscal period, across all sources. Superseded
        // revisions stay in the table (IsCurrent = 0) and remain reachable by history.
        builder.HasIndex(a => new { a.KpiId, a.FiscalPeriodId })
            .IsUnique()
            .HasFilter("[IsCurrent] = 1");

        builder.HasIndex(a => a.RecordedBy);
    }
}
