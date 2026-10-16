using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class UnitsOfMeasureConfiguration : IEntityTypeConfiguration<UnitsOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitsOfMeasure> builder)
    {
        builder.Property(u => u.Name).HasMaxLength(50).IsRequired();
        builder.Property(u => u.Symbol).HasMaxLength(20);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.SortOrder).IsRequired();

        builder.HasIndex(u => u.Name).IsUnique();
    }
}
