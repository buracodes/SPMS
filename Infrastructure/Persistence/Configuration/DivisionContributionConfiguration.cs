using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class DivisionContributionConfiguration : IEntityTypeConfiguration<DivisionContribution>
{
    public void Configure(EntityTypeBuilder<DivisionContribution> builder)
    {
        builder.HasOne(c => c.DivisionPlan)
            .WithMany(p => p.Contributions)
            .HasForeignKey(c => c.DivisionPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Scorecard)
            .WithMany()
            .HasForeignKey(c => c.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Goal)
            .WithMany()
            .HasForeignKey(c => c.GoalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.DivisionPlanId, c.GoalId }).IsUnique();
    }
}
