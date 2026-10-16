using Application.CQRS.PeriodLock.Handlers.Queries;
using Application.CQRS.PeriodLock.Requests.Queries;
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

public class GetPeriodLockStatusQueryHandlerTests
{
    [Fact]
    public async Task GetStatus_ReturnsTwelvePeriodsWithStates()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var handler = new GetPeriodLockStatusQueryHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new FiscalCalendarService());

        var response = await handler.Handle(new GetPeriodLockStatusQueryRequest
        {
            ScorecardId = fixture.ScorecardId,
            FiscalYearId = fixture.FiscalYearId
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(12, response.Data!.Periods.Count);
        Assert.All(response.Data.Periods, p => Assert.Equal(PeriodLockState.Locked, p.State));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, response.Data.Periods.Select(p => p.FiscalMonth).ToArray());
        Assert.Equal(new[] { 3, 6, 9, 12 }, response.Data.Periods.Where(p => p.IsQuarterEnd).Select(p => p.FiscalMonth).ToArray());
        Assert.Equal(12, response.Data.Periods.Single(p => p.IsYearEnd).FiscalMonth);
    }

    [Fact]
    public async Task GetStatus_ReflectsExplicitLock()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);

        var july = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);
        lease.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = fixture.ScorecardId,
            FiscalPeriodId = july.Id,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2,
            LockedAtUtc = new DateTime(2025, 7, 20, 8, 0, 0, DateTimeKind.Utc)
        });
        await lease.Context.SaveChangesAsync();

        var handler = new GetPeriodLockStatusQueryHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new FiscalCalendarService());

        var response = await handler.Handle(new GetPeriodLockStatusQueryRequest
        {
            ScorecardId = fixture.ScorecardId,
            FiscalYearId = fixture.FiscalYearId
        }, CancellationToken.None);

        var julyStatus = response.Data!.Periods.Single(p => p.FiscalMonth == 1);
        Assert.Equal(PeriodLockState.Locked, julyStatus.State);
        Assert.True(julyStatus.HasExplicitOverride);
    }

    [Fact]
    public async Task UnknownScorecard_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var handler = new GetPeriodLockStatusQueryHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new FiscalCalendarService());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPeriodLockStatusQueryRequest
            {
                ScorecardId = Guid.NewGuid(),
                FiscalYearId = fixture.FiscalYearId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task FiscalYearMismatch_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);

        var otherYear = new FiscalCalendarService().CreateFiscalYear(2024);
        lease.Context.FiscalYears.Add(otherYear);
        await lease.Context.SaveChangesAsync();

        var handler = new GetPeriodLockStatusQueryHandler(
            new ScorecardRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            CreateLockService(lease),
            new FiscalCalendarService());

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new GetPeriodLockStatusQueryRequest
            {
                ScorecardId = fixture.ScorecardId,
                FiscalYearId = otherYear.Id
            }, CancellationToken.None));
    }

    private static PeriodLockStatusService CreateLockService(TestDbContextLease lease)
    {
        return new PeriodLockStatusService(
            new FiscalPeriodRepository(lease.Context),
            new PeriodLockConfigRepository(lease.Context),
            new SystemConfigRepository(lease.Context),
            NullLogger<PeriodLockStatusService>.Instance);
    }

    private static async Task<(Guid ScorecardId, Guid FiscalYearId)> CreateFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var periods = calendar.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Lock Status",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.FiscalPeriods.AddRange(periods);
        await lease.Context.SaveChangesAsync();

        return (scorecard.Id, fiscalYear.Id);
    }
}