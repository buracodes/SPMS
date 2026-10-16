using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class InitiativeDependencyConfiguration : IEntityTypeConfiguration<InitiativeDependency>
{
    public void Configure(EntityTypeBuilder<InitiativeDependency> builder)
    {
        builder.ToTable("InitiativeDependencies");

        builder.HasKey(d => d.Id);

        // The same prerequisite cannot be linked to the same initiative twice.
        builder.HasIndex(d => new { d.InitiativeId, d.DependsOnInitiativeId })
            .IsUnique();

        builder.HasOne(d => d.Initiative)
            .WithMany(i => i.Dependencies)
            .HasForeignKey(d => d.InitiativeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: two cascade paths from Initiatives into the same table are
        // rejected by SQL Server. Initiatives are soft-deleted anyway, and dependency rows are
        // removed explicitly through the dependency handlers.
        builder.HasOne(d => d.DependsOnInitiative)
            .WithMany(i => i.Dependents)
            .HasForeignKey(d => d.DependsOnInitiativeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
