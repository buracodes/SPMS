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

        builder.HasOne(g => g.Scorecard)
            .WithMany(s => s.Goals)
            .HasForeignKey(g => g.ScorecardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => new { g.ScorecardId, g.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
