using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> builder)
    {
        builder.Property(fy => fy.Label)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(fy => fy.FiscalYearStatus)
            .IsRequired();

        builder.HasIndex(fy => fy.Label)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(fy => new { fy.StartDate, fy.EndDate })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasMany(fy => fy.Periods)
            .WithOne(period => period.FiscalYear)
            .HasForeignKey(period => period.FiscalYearId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
