using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class DivisionGoalConfiguration : IEntityTypeConfiguration<DivisionGoal>
{
    public void Configure(EntityTypeBuilder<DivisionGoal> builder)
    {
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(1000);
        builder.Property(g => g.ContributionDescription).HasMaxLength(2000);
        builder.Property(g => g.Weight).HasPrecision(5, 2);

        builder.HasOne(g => g.DivisionPlan)
            .WithMany(p => p.Goals)
            .HasForeignKey(g => g.DivisionPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.ParentGoal)
            .WithMany()
            .HasForeignKey(g => g.ParentGoalId)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft-deleted goals must not block reusing a name inside the same plan.
        builder.HasIndex(g => new { g.DivisionPlanId, g.Name })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
