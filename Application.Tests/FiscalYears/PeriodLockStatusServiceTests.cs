using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.FiscalYears;

public class PeriodLockStatusServiceTests
{
    [Fact]
    public async Task DefaultWindow_OpenInsideEntryWindow()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        var status = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 3, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PeriodLockState.Open, status.State);
        Assert.False(status.HasExplicitOverride);
        Assert.Equal(new DateTime(2025, 8, 1), status.EffectiveEntryOpenDate);
        Assert.Equal(new DateTime(2025, 8, 7), status.EffectiveCutoffDate);
    }

    [Fact]
    public async Task DefaultWindow_NotYetOpen_BeforeEntryOpen()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        var status = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 7, 15, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PeriodLockState.NotYetOpen, status.State);
    }

    [Fact]
    public async Task DefaultWindow_Locked_AfterCutoff()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        var status = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 10, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PeriodLockState.Locked, status.State);
    }

    [Fact]
    public async Task ExplicitLock_ForcesLocked()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        ctx.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = ctx.ScorecardId,
            FiscalPeriodId = ctx.Period.Id,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2,
            LockedAtUtc = new DateTime(2025, 7, 20, 8, 0, 0, DateTimeKind.Utc)
        });
        await ctx.Context.SaveChangesAsync();

        var status = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 3, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PeriodLockState.Locked, status.State);
        Assert.True(status.HasExplicitOverride);
    }

    [Fact]
    public async Task ExplicitUnlock_OverridesLock()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        ctx.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = ctx.ScorecardId,
            FiscalPeriodId = ctx.Period.Id,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2,
            LockedAtUtc = new DateTime(2025, 7, 20, 8, 0, 0, DateTimeKind.Utc),
            UnlockedAtUtc = new DateTime(2025, 7, 21, 8, 0, 0, DateTimeKind.Utc)
        });
        await ctx.Context.SaveChangesAsync();

        var status = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 3, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PeriodLockState.Open, status.State);
        Assert.True(status.HasExplicitOverride);
    }

    [Fact]
    public async Task CustomCutoffFromConfig_Used()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        ctx.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = ctx.ScorecardId,
            FiscalPeriodId = ctx.Period.Id,
            EntryOpenDay = 1,
            CutoffDay = 20,
            ReminderDaysBeforeCutoff = 3,
            LockedAtUtc = null
        });
        await ctx.Context.SaveChangesAsync();

        var statusOpen = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 15, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Open, statusOpen.State);
        Assert.Equal(new DateTime(2025, 8, 20), statusOpen.EffectiveCutoffDate);

        var statusLocked = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Locked, statusLocked.State);
    }

    [Fact]
    public async Task SystemConfigDefaults_UsedWhenNoExplicitConfig()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        ctx.Context.SystemConfigs.Add(new SystemConfig
        {
            Id = Guid.NewGuid(),
            Key = Domain.Constants.SystemConfigKeys.EntryOpenDay,
            ValueJson = "5",
            Category = Domain.Enums.SystemConfigCategory.EntryWindow
        });
        ctx.Context.SystemConfigs.Add(new SystemConfig
        {
            Id = Guid.NewGuid(),
            Key = Domain.Constants.SystemConfigKeys.CutoffDay,
            ValueJson = "10",
            Category = Domain.Enums.SystemConfigCategory.EntryWindow
        });
        await ctx.Context.SaveChangesAsync();

        var status = await service.GetStatusAsync(ctx.ScorecardId, ctx.Period, new DateTime(2025, 8, 12, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PeriodLockState.Locked, status.State);
        Assert.Equal(new DateTime(2025, 8, 5), status.EffectiveEntryOpenDate);
        Assert.Equal(new DateTime(2025, 8, 10), status.EffectiveCutoffDate);
    }

    [Fact]
    public async Task GetStatusesAsync_BatchesPeriodsWithConfigs()
    {
        var (service, ctx) = await CreateFixtureAsync();
        await using var _ = ctx;

        var periods = await ctx.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).ToListAsync();

        ctx.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = ctx.ScorecardId,
            FiscalPeriodId = periods[0].Id,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2,
            LockedAtUtc = new DateTime(2025, 7, 20, 8, 0, 0, DateTimeKind.Utc)
        });
        await ctx.Context.SaveChangesAsync();

        var configs = await new PeriodLockConfigRepository(ctx.Context).GetForScorecardFiscalYearAsync(ctx.ScorecardId, ctx.FiscalYearId);
        var statuses = await service.GetStatusesAsync(ctx.ScorecardId, periods, configs, new DateTime(2025, 8, 3, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(12, statuses.Count);
        Assert.Equal(PeriodLockState.Locked, statuses[0].State);
        Assert.Equal(PeriodLockState.NotYetOpen, statuses[1].State);
    }

    [Fact]
    public async Task FinalPeriod_GraceWindow_ExtendsCutoffToEndDatePlusSevenDays()
    {
        // Confirmed business rule (final-month grace): actual entry for the fiscal year's
        // last reporting month stays open through the end date plus seven calendar days,
        // inclusive, even when the configured cutoff would close the window earlier. A
        // year ending 2027-08-31 therefore accepts final-month entry on 2027-09-07 and
        // blocks it from 2027-09-08.
        var lease = await TestDbContextFactory.CreateAsync();
        await using var _ = lease;
        var calendar = new FiscalCalendarService();
        var fiscalYear = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "2026/27",
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2027, 8, 31)
        };
        var periods = calendar.GeneratePeriods(fiscalYear);
        var finalPeriod = periods.Single(p => p.FiscalMonth == 12);
        Assert.Equal(fiscalYear.EndDate.Date, finalPeriod.EndDate.Date);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Grace Test",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.FiscalPeriods.AddRange(periods);
        // A tighter configured cutoff must not override the grace deadline.
        lease.Context.SystemConfigs.Add(new SystemConfig
        {
            Id = Guid.NewGuid(),
            Key = Domain.Constants.SystemConfigKeys.CutoffDay,
            ValueJson = "5",
            Category = Domain.Enums.SystemConfigCategory.EntryWindow
        });
        await lease.Context.SaveChangesAsync();

        var lockService = new PeriodLockStatusService(
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            new SystemConfigRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            TestBusinessClock.Real(),
            NullLogger<PeriodLockStatusService>.Instance);

        var status = await lockService.GetStatusAsync(
            scorecard.Id, finalPeriod, new DateTime(2027, 9, 2, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Open, status.State);
        Assert.Equal(new DateTime(2027, 9, 1), status.EffectiveEntryOpenDate);
        Assert.Equal(fiscalYear.FinalMonthGraceDeadline, status.EffectiveCutoffDate);
        Assert.Equal(new DateTime(2027, 9, 7), status.EffectiveCutoffDate);

        var onDeadline = await lockService.GetStatusAsync(
            scorecard.Id, finalPeriod, new DateTime(2027, 9, 7, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Open, onDeadline.State);

        var afterGrace = await lockService.GetStatusAsync(
            scorecard.Id, finalPeriod, new DateTime(2027, 9, 8, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Locked, afterGrace.State);
    }

    [Fact]
    public async Task EarlierPeriod_DoesNotReceiveGraceExtension()
    {
        // Only the fiscal year's last reporting month gets the grace window; every other
        // month keeps the configured entry window so normal data-locking rules hold.
        var lease = await TestDbContextFactory.CreateAsync();
        await using var _ = lease;
        var calendar = new FiscalCalendarService();
        var fiscalYear = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "2026/27b",
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2027, 8, 31)
        };
        var periods = calendar.GeneratePeriods(fiscalYear);
        var firstPeriod = periods.Single(p => p.FiscalMonth == 1);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Grace Isolation Test",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.SystemConfigs.Add(new SystemConfig
        {
            Id = Guid.NewGuid(),
            Key = Domain.Constants.SystemConfigKeys.CutoffDay,
            ValueJson = "5",
            Category = Domain.Enums.SystemConfigCategory.EntryWindow
        });
        await lease.Context.SaveChangesAsync();

        var lockService = new PeriodLockStatusService(
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            new SystemConfigRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            TestBusinessClock.Real(),
            NullLogger<PeriodLockStatusService>.Instance);

        var status = await lockService.GetStatusAsync(
            scorecard.Id, firstPeriod, new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Open, status.State);
        Assert.Equal(new DateTime(2026, 10, 5), status.EffectiveCutoffDate);

        var locked = await lockService.GetStatusAsync(
            scorecard.Id, firstPeriod, new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Locked, locked.State);
    }

    [Fact]
    public async Task FinalPeriod_ExplicitConfig_StillTakesPrecedenceOverGrace()
    {
        // Authorized period extensions keep working: an explicit (unlocked) period-lock
        // config for the final month wins over both the grace deadline and the defaults.
        var lease = await TestDbContextFactory.CreateAsync();
        await using var _ = lease;
        var calendar = new FiscalCalendarService();
        var fiscalYear = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "2026/27c",
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2027, 8, 31)
        };
        var periods = calendar.GeneratePeriods(fiscalYear);
        var finalPeriod = periods.Single(p => p.FiscalMonth == 12);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Grace Extension Test",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            FiscalPeriodId = finalPeriod.Id,
            EntryOpenDay = 1,
            CutoffDay = 20,
            ReminderDaysBeforeCutoff = 3
        });
        await lease.Context.SaveChangesAsync();

        var lockService = new PeriodLockStatusService(
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            new SystemConfigRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            TestBusinessClock.Real(),
            NullLogger<PeriodLockStatusService>.Instance);

        var status = await lockService.GetStatusAsync(
            scorecard.Id, finalPeriod, new DateTime(2027, 9, 15, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(PeriodLockState.Open, status.State);
        Assert.True(status.HasExplicitOverride);
        Assert.Equal(new DateTime(2027, 9, 20), status.EffectiveCutoffDate);
    }

    private static async Task<(PeriodLockStatusService Service, TestLease Ctx)> CreateFixtureAsync()
    {
        var lease = await TestDbContextFactory.CreateAsync();
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var periods = calendar.GeneratePeriods(fiscalYear);
        var july = periods.First(p => p.FiscalMonth == 1);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Lock Test",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.FiscalPeriods.AddRange(periods);
        await lease.Context.SaveChangesAsync();

        var lockService = new PeriodLockStatusService(
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            new SystemConfigRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            TestBusinessClock.Real(),
            NullLogger<PeriodLockStatusService>.Instance);

        return (lockService, new TestLease(lease, scorecard.Id, july, fiscalYear.Id));
    }

    private sealed record TestLease(
        TestDbContextLease Inner,
        Guid ScorecardId,
        FiscalPeriod Period,
        Guid FiscalYearId) : IAsyncDisposable
    {
        public SpmsDbContext Context => Inner.Context;
        public async ValueTask DisposeAsync() => await Inner.DisposeAsync();
    }
}