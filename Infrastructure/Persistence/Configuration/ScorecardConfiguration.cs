using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class ScorecardConfiguration : IEntityTypeConfiguration<Scorecard>
{
    public void Configure(EntityTypeBuilder<Scorecard> builder)
    {
        builder.Property(scorecard => scorecard.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(scorecard => scorecard.Description)
            .HasMaxLength(1000);

        builder.Property(scorecard => scorecard.Scope)
            .IsRequired();

        builder.Property(scorecard => scorecard.ScorecardStatus)
            .IsRequired();

        builder.HasOne(scorecard => scorecard.FiscalYear)
            .WithMany(fiscalYear => fiscalYear.Scorecards)
            .HasForeignKey(scorecard => scorecard.FiscalYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(scorecard => new { scorecard.FiscalYearId, scorecard.Name })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
