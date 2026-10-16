using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> builder)
    {
        builder.ToTable("Milestones");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.Description)
            .HasMaxLength(2000);

        builder.Property(m => m.Comments)
            .HasMaxLength(4000);

        builder.Property(m => m.Weight)
            .HasPrecision(9, 4);

        builder.HasIndex(m => m.InitiativeId);

        builder.HasOne(m => m.Initiative)
            .WithMany(i => i.Milestones)
            .HasForeignKey(m => m.InitiativeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
