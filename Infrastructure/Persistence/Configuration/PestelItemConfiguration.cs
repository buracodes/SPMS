using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class PestelItemConfiguration : IEntityTypeConfiguration<PestelItem>
{
    public void Configure(EntityTypeBuilder<PestelItem> builder)
    {
        builder.Property(pi => pi.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(pi => pi.Description)
            .HasMaxLength(1500);

        builder.Property(pi => pi.Category)
            .IsRequired();

        builder.Property(pi => pi.Impact)
            .IsRequired();
    }
}
