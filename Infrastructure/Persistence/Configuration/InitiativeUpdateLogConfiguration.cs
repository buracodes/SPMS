using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class InitiativeUpdateLogConfiguration : IEntityTypeConfiguration<InitiativeUpdateLog>
{
    public void Configure(EntityTypeBuilder<InitiativeUpdateLog> builder)
    {
        builder.ToTable("InitiativeUpdateLog");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ChangeType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.Details)
            .HasMaxLength(4000);

        builder.Property(l => l.PreviousValue)
            .HasMaxLength(200);

        builder.Property(l => l.NewValue)
            .HasMaxLength(200);

        // Progress history is read newest-first, per initiative and per milestone.
        builder.HasIndex(l => new { l.InitiativeId, l.CreatedDate });
        builder.HasIndex(l => new { l.MilestoneId, l.CreatedDate });

        builder.HasOne(l => l.Initiative)
            .WithMany(i => i.UpdateLogs)
            .HasForeignKey(l => l.InitiativeId)
            .OnDelete(DeleteBehavior.Cascade);

        // A log entry outlives the milestone it described. Deactivating a milestone is a soft delete
        // (IsDeleted = true), so the row is never physically removed and the FK stays satisfied.
        // NoAction remains as the guard rail: if a physical delete is ever attempted it fails loudly
        // with SQL Server error 547 rather than silently orphaning the audit trail. SetNull is not an
        // option here - Initiative already cascades to both Milestones and InitiativeUpdateLog, so
        // SET NULL would be a second cascade path (SQL Server error 1785).
        builder.HasOne(l => l.Milestone)
            .WithMany()
            .HasForeignKey(l => l.MilestoneId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
