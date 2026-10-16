using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> builder)
    {
        builder.Property(period => period.FiscalMonth)
            .IsRequired();

        builder.Property(period => period.Quarter)
            .IsRequired();

        builder.Property(period => period.IsQuarterEnd)
            .IsRequired();

        builder.Property(period => period.IsYearEnd)
            .IsRequired();

        builder.HasIndex(period => new { period.FiscalYearId, period.FiscalMonth })
            .IsUnique();

        builder.HasIndex(period => new { period.FiscalYearId, period.StartDate, period.EndDate })
            .IsUnique();
    }
}
