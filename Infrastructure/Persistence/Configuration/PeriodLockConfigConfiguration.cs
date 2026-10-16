using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class PeriodLockConfigConfiguration : IEntityTypeConfiguration<PeriodLockConfig>
{
    public void Configure(EntityTypeBuilder<PeriodLockConfig> builder)
    {
        builder.Property(c => c.EntryOpenDay).IsRequired();
        builder.Property(c => c.CutoffDay).IsRequired();
        builder.Property(c => c.ReminderDaysBeforeCutoff).IsRequired();

        builder.HasOne(c => c.Scorecard)
            .WithMany()
            .HasForeignKey(c => c.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.FiscalPeriod)
            .WithMany()
            .HasForeignKey(c => c.FiscalPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.LockedByUser)
            .WithMany()
            .HasForeignKey(c => c.LockedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.UnlockedByUser)
            .WithMany()
            .HasForeignKey(c => c.UnlockedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.ScorecardId, c.FiscalPeriodId }).IsUnique();
        builder.HasIndex(c => c.FiscalPeriodId);
    }
}
