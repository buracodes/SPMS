using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
    public void Configure(EntityTypeBuilder<Division> builder)
    {
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Code).HasMaxLength(50).IsRequired();

        builder.HasIndex(d => d.Name).IsUnique();
        builder.HasIndex(d => d.Code).IsUnique();
    }
}
