using Application.CQRS.PeriodLock.Handlers.Commands;
using Application.CQRS.PeriodLock.Requests.Commands;
using Application.DTOS.PeriodLock;
using Application.Exceptions;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.FiscalYears;

public class SetPeriodLockConfigCommandHandlerTests
{
    [Fact]
    public async Task SetConfig_CreatesNewConfig()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);
        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        var response = await handler.Handle(new SetPeriodLockConfigCommandRequest
        {
            Dto = new SetPeriodLockConfigDto
            {
                ScorecardId = scorecardId,
                FiscalPeriodId = july.Id,
                EntryOpenDay = 5,
                CutoffDay = 10,
                ReminderDaysBeforeCutoff = 3,
                Lock = true
            }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(PeriodLockState.Locked, response.Data!.State);
        Assert.Equal(5, response.Data.EffectiveEntryOpenDay);
        Assert.Equal(10, response.Data.EffectiveCutoffDay);

        var saved = await lease.Context.PeriodLockConfigs.SingleAsync();
        Assert.Equal(5, saved.EntryOpenDay);
        Assert.Equal(10, saved.CutoffDay);
        Assert.Equal(3, saved.ReminderDaysBeforeCutoff);
        Assert.NotNull(saved.LockedAtUtc);
        Assert.NotNull(saved.LockedByUserId);
    }

    [Fact]
    public async Task SetConfig_UpdateExisting()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);
        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);

        lease.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            FiscalPeriodId = july.Id,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2
        });
        await lease.Context.SaveChangesAsync();

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        var response = await handler.Handle(new SetPeriodLockConfigCommandRequest
        {
            Dto = new SetPeriodLockConfigDto
            {
                ScorecardId = scorecardId,
                FiscalPeriodId = july.Id,
                EntryOpenDay = 3,
                CutoffDay = 15,
                ReminderDaysBeforeCutoff = 5
            }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(PeriodLockState.Locked, response.Data!.State);
        Assert.Equal(3, response.Data.EffectiveEntryOpenDay);
        Assert.Equal(15, response.Data.EffectiveCutoffDay);

        var saved = await lease.Context.PeriodLockConfigs.SingleAsync();
        Assert.Equal(3, saved.EntryOpenDay);
        Assert.Equal(15, saved.CutoffDay);
        Assert.Equal(5, saved.ReminderDaysBeforeCutoff);
    }

    [Fact]
    public async Task LockAndUnlock_Conflict_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);
        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new SetPeriodLockConfigCommandRequest
            {
                Dto = new SetPeriodLockConfigDto
                {
                    ScorecardId = scorecardId,
                    FiscalPeriodId = july.Id,
                    Lock = true,
                    Unlock = true
                }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Lock_ExplicitlyLocksPeriod()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);
        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        var response = await handler.Handle(new SetPeriodLockConfigCommandRequest
        {
            Dto = new SetPeriodLockConfigDto
            {
                ScorecardId = scorecardId,
                FiscalPeriodId = july.Id,
                Lock = true
            }
        }, CancellationToken.None);

        Assert.Equal(PeriodLockState.Locked, response.Data!.State);
        Assert.True(response.Data.HasExplicitOverride);

        var saved = await lease.Context.PeriodLockConfigs.SingleAsync();
        Assert.NotNull(saved.LockedAtUtc);
        Assert.NotNull(saved.LockedByUserId);
        Assert.Null(saved.UnlockedAtUtc);
    }

    [Fact]
    public async Task Unlock_RemovesLock()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);
        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        await handler.Handle(new SetPeriodLockConfigCommandRequest
        {
            Dto = new SetPeriodLockConfigDto
            {
                ScorecardId = scorecardId,
                FiscalPeriodId = july.Id,
                Lock = true
            }
        }, CancellationToken.None);

        var unlockResponse = await handler.Handle(new SetPeriodLockConfigCommandRequest
        {
            Dto = new SetPeriodLockConfigDto
            {
                ScorecardId = scorecardId,
                FiscalPeriodId = july.Id,
                Unlock = true
            }
        }, CancellationToken.None);

        Assert.Equal(PeriodLockState.Open, unlockResponse.Data!.State);
        Assert.True(unlockResponse.Data.HasExplicitOverride);

        var saved = await lease.Context.PeriodLockConfigs.SingleAsync();
        Assert.NotNull(saved.UnlockedAtUtc);
        Assert.NotNull(saved.UnlockedByUserId);
    }

    [Fact]
    public async Task UnknownScorecard_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);
        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetPeriodLockConfigCommandRequest
            {
                Dto = new SetPeriodLockConfigDto
                {
                    ScorecardId = Guid.NewGuid(),
                    FiscalPeriodId = july.Id,
                    Lock = true
                }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task PeriodFromOtherFiscalYear_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (scorecardId, fiscalYearId, userId) = await CreateFixtureAsync(lease);

        var otherYear = new FiscalCalendarService().CreateFiscalYear(2024);
        var otherPeriods = new FiscalCalendarService().GeneratePeriods(otherYear);
        lease.Context.FiscalYears.Add(otherYear);
        lease.Context.FiscalPeriods.AddRange(otherPeriods);
        await lease.Context.SaveChangesAsync();

        var otherJuly = otherPeriods.First(p => p.FiscalMonth == 1);

        var handler = new SetPeriodLockConfigCommandHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new TestCurrentUserService { UserId = userId });

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new SetPeriodLockConfigCommandRequest
            {
                Dto = new SetPeriodLockConfigDto
                {
                    ScorecardId = scorecardId,
                    FiscalPeriodId = otherJuly.Id,
                    Lock = true
                }
            }, CancellationToken.None));
    }

    private static PeriodLockStatusService CreateLockService(TestDbContextLease lease)
    {
        return new PeriodLockStatusService(
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            new SystemConfigRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            TestBusinessClock.Real(),
            NullLogger<PeriodLockStatusService>.Instance);
    }

    private static async Task<(Guid ScorecardId, Guid FiscalYearId, Guid UserId)> CreateFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var periods = calendar.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Lock Config",
            Scope = ScorecardScope.Corporate
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            FullName = "Test User",
            PasswordHash = "hash",
            Email = "test@example.com"
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Users.Add(user);
        await lease.Context.SaveChangesAsync();

        return (scorecard.Id, fiscalYear.Id, user.Id);
    }
}