using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class DivisionPlanConfiguration : IEntityTypeConfiguration<DivisionPlan>
{
    public void Configure(EntityTypeBuilder<DivisionPlan> builder)
    {
        builder.Property(p => p.PlanStatus).IsRequired();

        builder.HasOne(p => p.Division)
            .WithMany(d => d.Plans)
            .HasForeignKey(p => p.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Scorecard)
            .WithMany()
            .HasForeignKey(p => p.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.DivisionId, p.ScorecardId }).IsUnique();
    }
}
