using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class InitiativeConfiguration : IEntityTypeConfiguration<Initiative>
{
    public void Configure(EntityTypeBuilder<Initiative> builder)
    {
        // StartDate/EndDate are required and the window is ordered: a Start date after the End date
        // has no valid execution timeline. The API enforces this with InitiativeGuard.ValidateDateRange;
        // this constraint is the schema-level backstop so no write path can persist an inverted window.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Initiatives_DateWindow",
            "([StartDate] IS NOT NULL AND [EndDate] IS NOT NULL AND [StartDate] <= [EndDate])"));

        // Note: Initiative re-declares Status as InitiativeStatus, which shadows the inherited
        // CommonEntity.Status (EntityStatus). Because the derived property hides the base one,
        // EF only ever sees the InitiativeStatus property, so `Initiatives.Status` holds
        // InitiativeStatus and CommonEntity.Status is not a column here. Do not call
        // Ignore(i => i.Status) - that would drop the status column.

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.Description)
            .HasMaxLength(2000);

        builder.Property(i => i.Resources)
            .HasMaxLength(2000);

        builder.Property(i => i.LessonsLearned)
            .HasMaxLength(4000);

        builder.Property(i => i.OutcomeSummary)
            .HasMaxLength(4000);

        builder.Property(i => i.Budget)
            .HasPrecision(18, 2);

        builder.Property(i => i.ActualExpenditure)
            .HasPrecision(18, 2);

        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.OwnerId);

        // Strategic links are optional (FR-401): Restrict so deleting a Goal/KRA/KPI cannot
        // silently orphan or cascade-delete execution history.
        builder.HasOne(i => i.Goal)
            .WithMany()
            .HasForeignKey(i => i.GoalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Kra)
            .WithMany()
            .HasForeignKey(i => i.KraId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Kpi)
            .WithMany()
            .HasForeignKey(i => i.KpiId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Owner)
            .WithMany()
            .HasForeignKey(i => i.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
