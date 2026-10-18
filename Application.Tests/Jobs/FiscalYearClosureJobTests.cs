using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Jobs;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Jobs;

public class FiscalYearClosureJobTests
{
    [Fact]
    public async Task ClosesCompletedFiscalYear_AndItsScorecards()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var completedYear = NewFiscalYear(2025);
        var currentYear = NewFiscalYear(2099);

        var completedScorecard = NewScorecard(completedYear.Id, "Corporate 2025");
        var currentScorecard = NewScorecard(currentYear.Id, "Corporate 2099");

        lease.Context.FiscalYears.Add(completedYear);
        lease.Context.FiscalYears.Add(currentYear);
        lease.Context.Scorecards.Add(completedScorecard);
        lease.Context.Scorecards.Add(currentScorecard);
        await lease.Context.SaveChangesAsync();

        await new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            TestBusinessClock.Real()).ExecuteAsync();

        var years = await lease.Context.FiscalYears.AsNoTracking().ToDictionaryAsync(y => y.Id);
        var scorecards = await lease.Context.Scorecards.AsNoTracking().ToDictionaryAsync(s => s.Id);

        Assert.Equal(FiscalYearStatus.Closed, years[completedYear.Id].FiscalYearStatus);
        Assert.Equal(ScorecardStatus.Closed, scorecards[completedScorecard.Id].ScorecardStatus);

        Assert.Equal(FiscalYearStatus.Open, years[currentYear.Id].FiscalYearStatus);
        Assert.Equal(ScorecardStatus.Active, scorecards[currentScorecard.Id].ScorecardStatus);
    }

    [Fact]
    public async Task DoesNotTouchYearWhoseEndDateHasNotPassed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var currentYear = NewFiscalYear(2099);

        lease.Context.FiscalYears.Add(currentYear);
        lease.Context.Scorecards.Add(NewScorecard(currentYear.Id, "Corporate 2099"));
        await lease.Context.SaveChangesAsync();

        await new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            TestBusinessClock.Real()).ExecuteAsync();

        var year = await lease.Context.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == currentYear.Id);
        var scorecard = await lease.Context.Scorecards.AsNoTracking().SingleAsync(s => s.FiscalYearId == currentYear.Id);

        Assert.Equal(FiscalYearStatus.Open, year.FiscalYearStatus);
        Assert.Equal(ScorecardStatus.Active, scorecard.ScorecardStatus);
    }

    [Fact]
    public async Task ClosesYear_WhenGracePeriodExpires_EvenIfUtcDateHasNot()
    {
        // Timezone boundary (FR-703): the year 2025/26 ends 2026-06-30, so its final-month
        // grace deadline is 2026-07-07 and regular entry is blocked from 2026-07-08. At
        // 22:00 UTC on 2026-07-08 it is already 01:00 on 2026-07-09 in East Africa Time,
        // so in business time the grace has expired even though the UTC calendar date
        // would only expire it later that day.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var completedYear = NewFiscalYear(2025);
        lease.Context.FiscalYears.Add(completedYear);
        lease.Context.Scorecards.Add(NewScorecard(completedYear.Id, "Corporate 2025"));
        await lease.Context.SaveChangesAsync();

        var clock = TestBusinessClock.On(new DateTime(2026, 7, 8, 22, 0, 0, DateTimeKind.Utc));
        await new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            clock).ExecuteAsync();

        var year = await lease.Context.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == completedYear.Id);
        var scorecard = await lease.Context.Scorecards.AsNoTracking().SingleAsync(s => s.FiscalYearId == completedYear.Id);
        Assert.Equal(FiscalYearStatus.Closed, year.FiscalYearStatus);
        Assert.Equal(ScorecardStatus.Closed, scorecard.ScorecardStatus);
    }

    [Fact]
    public async Task DoesNotCloseYear_DuringFinalMonthGracePeriod()
    {
        // Confirmed business rule (final-month grace): the year — and its scorecards —
        // stay writable until the end date plus seven calendar days (2026-07-07), so the
        // closure job must not close anything on 2026-07-05 even though the end date has
        // already passed.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var completedYear = NewFiscalYear(2025);
        lease.Context.FiscalYears.Add(completedYear);
        lease.Context.Scorecards.Add(NewScorecard(completedYear.Id, "Corporate 2025"));
        await lease.Context.SaveChangesAsync();

        var clock = TestBusinessClock.On(new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc));
        await new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            clock).ExecuteAsync();

        var year = await lease.Context.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == completedYear.Id);
        var scorecard = await lease.Context.Scorecards.AsNoTracking().SingleAsync(s => s.FiscalYearId == completedYear.Id);
        Assert.Equal(FiscalYearStatus.Open, year.FiscalYearStatus);
        Assert.Equal(ScorecardStatus.Active, scorecard.ScorecardStatus);
    }

    [Fact]
    public async Task DoesNotCloseYear_WhileBusinessDateIsStillTheEndDate()
    {
        // The other side of the boundary: at 20:59:59 UTC (23:59:59 EAT) on 2026-06-30 the
        // business date is still the end date, so the year must remain Open until the next
        // business day.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var completedYear = NewFiscalYear(2025);
        lease.Context.FiscalYears.Add(completedYear);
        lease.Context.Scorecards.Add(NewScorecard(completedYear.Id, "Corporate 2025"));
        await lease.Context.SaveChangesAsync();

        var clock = TestBusinessClock.On(new DateTime(2026, 6, 30, 20, 59, 59, DateTimeKind.Utc));
        await new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            clock).ExecuteAsync();

        var year = await lease.Context.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == completedYear.Id);
        Assert.Equal(FiscalYearStatus.Open, year.FiscalYearStatus);
    }

    [Fact]
    public async Task IsIdempotent_WhenRunTwice()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var completedYear = NewFiscalYear(2025);
        lease.Context.FiscalYears.Add(completedYear);
        lease.Context.Scorecards.Add(NewScorecard(completedYear.Id, "Corporate 2025"));
        await lease.Context.SaveChangesAsync();

        var job = new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            TestBusinessClock.Real());

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var year = await lease.Context.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == completedYear.Id);
        var scorecard = await lease.Context.Scorecards.AsNoTracking().SingleAsync(s => s.FiscalYearId == completedYear.Id);

        Assert.Equal(FiscalYearStatus.Closed, year.FiscalYearStatus);
        Assert.Equal(ScorecardStatus.Closed, scorecard.ScorecardStatus);
    }

    [Fact]
    public async Task KeepsManuallyClosedScorecard_Closed_WhenYearCloses()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var completedYear = NewFiscalYear(2025);
        var scorecard = NewScorecard(completedYear.Id, "Already Closed");
        scorecard.ScorecardStatus = ScorecardStatus.Closed;

        lease.Context.FiscalYears.Add(completedYear);
        lease.Context.Scorecards.Add(scorecard);
        await lease.Context.SaveChangesAsync();

        await new FiscalYearClosureJob(
            CreateScopeFactory(lease),
            NullLogger<FiscalYearClosureJob>.Instance,
            TestBusinessClock.Real()).ExecuteAsync();

        var result = await lease.Context.Scorecards.AsNoTracking().SingleAsync(s => s.Id == scorecard.Id);
        Assert.Equal(ScorecardStatus.Closed, result.ScorecardStatus);
    }

    private static FiscalYear NewFiscalYear(int startYear)
    {
        var calendar = new Application.Services.FiscalCalendarService();
        var year = calendar.CreateFiscalYear(startYear);
        year.Periods = calendar.GeneratePeriods(year).ToList();
        return year;
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

    private static IServiceScopeFactory CreateScopeFactory(TestDbContextLease lease)
    {
        var services = new ServiceCollection()
            .AddDbContext<SpmsDbContext>(options => options.UseSqlite(lease.Connection))
            .BuildServiceProvider();
        return services.GetRequiredService<IServiceScopeFactory>();
    }
}