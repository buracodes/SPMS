using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class SwotItemConfiguration : IEntityTypeConfiguration<SwotItem>
{
    public void Configure(EntityTypeBuilder<SwotItem> builder)
    {
        builder.Property(si => si.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(si => si.Description)
            .HasMaxLength(1500);

        builder.Property(si => si.Category)
            .IsRequired();

        builder.Property(si => si.Impact)
            .IsRequired();
    }
}
