using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class CoreValueConfiguration : IEntityTypeConfiguration<CoreValue>
{
    public void Configure(EntityTypeBuilder<CoreValue> builder)
    {
        builder.Property(cv => cv.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(cv => cv.Description)
            .HasMaxLength(1000);
    }
}
