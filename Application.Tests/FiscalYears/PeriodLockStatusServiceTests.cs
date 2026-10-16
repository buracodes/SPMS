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