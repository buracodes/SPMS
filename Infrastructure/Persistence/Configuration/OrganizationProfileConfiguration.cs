using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class OrganizationProfileConfiguration : IEntityTypeConfiguration<OrganizationProfile>
{
    public void Configure(EntityTypeBuilder<OrganizationProfile> builder)
    {
        builder.Property(op => op.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(op => op.Description)
            .HasMaxLength(1000);

        builder.Property(op => op.Vision)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(op => op.Mission)
            .HasMaxLength(2000)
            .IsRequired();

        builder.HasMany(op => op.CoreValues)
            .WithOne(cv => cv.OrganizationProfile)
            .HasForeignKey(cv => cv.OrganizationProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(op => op.StrategicThemes)
            .WithOne(st => st.OrganizationProfile)
            .HasForeignKey(st => st.OrganizationProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(op => op.SwotItems)
            .WithOne(si => si.OrganizationProfile)
            .HasForeignKey(si => si.OrganizationProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(op => op.PestelItems)
            .WithOne(pi => pi.OrganizationProfile)
            .HasForeignKey(pi => pi.OrganizationProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
