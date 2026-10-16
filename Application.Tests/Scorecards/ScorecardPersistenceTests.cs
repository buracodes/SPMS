using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Scorecards;

public class ScorecardPersistenceTests
{
    [Fact]
    public async Task Scorecard_BelongsToFiscalYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fiscalYear = new FiscalCalendarService().CreateFiscalYear(2025);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Corporate Scorecard",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        await lease.Context.SaveChangesAsync();

        var persisted = await lease.Context.Scorecards
            .Include(item => item.FiscalYear)
            .SingleAsync();

        Assert.Equal("Corporate Scorecard", persisted.Name);
        Assert.Equal(ScorecardScope.Corporate, persisted.Scope);
        Assert.Equal(ScorecardStatus.Active, persisted.ScorecardStatus);
        Assert.Equal("2025/26", persisted.FiscalYear!.Label);
    }

    [Fact]
    public async Task Scorecard_NameMustBeUniqueWithinFiscalYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fiscalYear = new FiscalCalendarService().CreateFiscalYear(2025);

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.AddRange(
            NewScorecard(fiscalYear.Id, "Corporate Scorecard"),
            NewScorecard(fiscalYear.Id, "Corporate Scorecard"));

        await Assert.ThrowsAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Scorecard_SameNameAllowedAcrossDifferentFiscalYears()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear2025 = service.CreateFiscalYear(2025);
        var fiscalYear2026 = service.CreateFiscalYear(2026);

        lease.Context.FiscalYears.AddRange(fiscalYear2025, fiscalYear2026);
        lease.Context.Scorecards.AddRange(
            NewScorecard(fiscalYear2025.Id, "Corporate Scorecard"),
            NewScorecard(fiscalYear2026.Id, "Corporate Scorecard"));

        await lease.Context.SaveChangesAsync();

        Assert.Equal(2, await lease.Context.Scorecards.CountAsync());
    }

    private static Scorecard NewScorecard(Guid fiscalYearId, string name)
    {
        return new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYearId,
            Name = name,
            Scope = ScorecardScope.Corporate
        };
    }
}
