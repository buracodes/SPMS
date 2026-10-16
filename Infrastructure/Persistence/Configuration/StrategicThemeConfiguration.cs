using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class StrategicThemeConfiguration : IEntityTypeConfiguration<StrategicTheme>
{
    public void Configure(EntityTypeBuilder<StrategicTheme> builder)
    {
        builder.Property(st => st.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(st => st.Code)
            .HasMaxLength(50);

        builder.Property(st => st.Description)
            .HasMaxLength(1000);

        builder.Property(st => st.Color)
            .HasMaxLength(50);
    }
}
