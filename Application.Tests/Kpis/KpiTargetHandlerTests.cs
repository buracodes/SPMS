using Application.CQRS.KpiTargets.Handlers.Commands;
using Application.CQRS.KpiTargets.Handlers.Queries;
using Application.CQRS.KpiTargets.Requests.Commands;
using Application.CQRS.KpiTargets.Requests.Queries;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Application.Validation.KpiTarget;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiTargetHandlerTests
{
    private static (Guid fiscalYearId, Guid scorecardId, Guid kraId, Guid kpiId) CreateFixture(TestDbContextLease lease)
    {
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue Growth",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.SaveChanges();

        return (fiscalYear.Id, scorecard.Id, kra.Id, kpi.Id);
    }

    private static SetKpiTargetsCommandHandler CreateHandler(TestDbContextLease lease, IFiscalCalendarService? calendar = null)
    {
        return new SetKpiTargetsCommandHandler(
            new KpiRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            calendar ?? new FiscalCalendarService(),
            new TestSpmsJobScheduler());
    }

    private static SetKpiTargetsDto CreateDto(Guid kpiId, Guid fiscalYearId, bool includeQuarterly = true)
    {
        return new SetKpiTargetsDto
        {
            KpiId = kpiId,
            FiscalYearId = fiscalYearId,
            MonthlyTargets = Enumerable.Range(1, 12).Select(m => (decimal)m).ToList(),
            QuarterlyTargets = includeQuarterly ? Enumerable.Range(1, 4).Select(q => (decimal)q * 3).ToList() : null,
            AnnualTarget = 100
        };
    }

    [Fact]
    public async Task SetKpiTargets_CreatesMonthlyQuarterlyAnnualTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var handler = CreateHandler(lease);
        var response = await handler.Handle(new SetKpiTargetsCommandRequest { Dto = CreateDto(kpiId, fiscalYearId) }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(17, response.Data!.Targets.Count);

        var targets = await lease.Context.KpiTargets.ToListAsync();
        Assert.Equal(17, targets.Count);
        Assert.Equal(12, targets.Count(t => t.Granularity == TargetGranularity.Monthly));
        Assert.Equal(4, targets.Count(t => t.Granularity == TargetGranularity.Quarterly));
        Assert.Equal(1, targets.Count(t => t.Granularity == TargetGranularity.Annual));
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Annual && t.TargetValue == 100);
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Quarterly && t.TargetValue == 9); // Q3 = month 9
    }

    [Fact]
    public async Task SetKpiTargets_ReplaceRemovesPreviousTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var handler = CreateHandler(lease);
        await handler.Handle(new SetKpiTargetsCommandRequest { Dto = CreateDto(kpiId, fiscalYearId) }, CancellationToken.None);

        var updated = CreateDto(kpiId, fiscalYearId);
        updated.AnnualTarget = 250;
        var response = await handler.Handle(new SetKpiTargetsCommandRequest { Dto = updated }, CancellationToken.None);

        var targets = await lease.Context.KpiTargets.ToListAsync();
        Assert.Equal(17, targets.Count);
        Assert.Equal(250, targets.First(t => t.Granularity == TargetGranularity.Annual).TargetValue);
    }

    [Fact]
    public async Task SetKpiTargets_UpdateEditsExistingRows_NoDuplicates()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var handler = CreateHandler(lease);
        await handler.Handle(new SetKpiTargetsCommandRequest { Dto = CreateDto(kpiId, fiscalYearId) }, CancellationToken.None);

        var firstIds = (await lease.Context.KpiTargets
            .Where(t => t.KpiId == kpiId)
            .Select(t => t.Id)
            .ToListAsync())
            .ToHashSet();

        var updated = CreateDto(kpiId, fiscalYearId);
        updated.AnnualTarget = 250;
        updated.QuarterlyTargets![0] = 42;
        updated.MonthlyTargets[0] = 55;
        await handler.Handle(new SetKpiTargetsCommandRequest { Dto = updated }, CancellationToken.None);

        var secondIds = (await lease.Context.KpiTargets
            .Where(t => t.KpiId == kpiId)
            .Select(t => t.Id)
            .ToListAsync())
            .ToHashSet();

        // The same physical rows must be edited in place, not deleted and re-created.
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(17, secondIds.Count);

        var annual = await lease.Context.KpiTargets
            .SingleAsync(t => t.KpiId == kpiId && t.Granularity == TargetGranularity.Annual);
        Assert.Equal(250, annual.TargetValue);

        var q1 = await lease.Context.KpiTargets
            .SingleAsync(t => t.KpiId == kpiId && t.Granularity == TargetGranularity.Quarterly && t.FiscalPeriod!.FiscalMonth == 3);
        Assert.Equal(42, q1.TargetValue);
    }

    [Fact]
    public async Task SetKpiTargets_UnknownKpi_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var handler = CreateHandler(lease);
        var dto = CreateDto(Guid.NewGuid(), fiscalYearId);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetKpiTargetsCommandRequest { Dto = dto }, CancellationToken.None));
    }

    [Fact]
    public async Task SetKpiTargets_KpiFromOtherFiscalYear_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var otherYear = new FiscalCalendarService().CreateFiscalYear(2026);
        lease.Context.FiscalYears.Add(otherYear);
        await lease.Context.SaveChangesAsync();

        var handler = CreateHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new SetKpiTargetsCommandRequest { Dto = CreateDto(kpiId, otherYear.Id) }, CancellationToken.None));
    }

    [Fact]
    public async Task SetKpiTargets_GeneratesPeriodsWhenMissing()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, scorecardId, kraId, kpiId) = CreateFixture(lease);
        var fiscalYear = await lease.Context.FiscalYears.SingleAsync();

        lease.Context.FiscalPeriods.RemoveRange(lease.Context.FiscalPeriods);
        await lease.Context.SaveChangesAsync();

        var handler = CreateHandler(lease);
        var response = await handler.Handle(new SetKpiTargetsCommandRequest { Dto = CreateDto(kpiId, fiscalYear.Id) }, CancellationToken.None);

        Assert.Equal(17, response.Data!.Targets.Count);
        Assert.Equal(12, await lease.Context.FiscalPeriods.CountAsync());
    }

    [Fact]
    public async Task GetKpiTargets_ReturnsTargetsForFiscalYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var setHandler = CreateHandler(lease);
        await setHandler.Handle(new SetKpiTargetsCommandRequest { Dto = CreateDto(kpiId, fiscalYearId) }, CancellationToken.None);

        var getHandler = new GetKpiTargetsQueryHandler(
            new KpiRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context));

        var response = await getHandler.Handle(new GetKpiTargetsQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(17, response.Data!.Targets.Count);
        Assert.Contains(response.Data.Targets, t => t.Granularity == TargetGranularity.Annual && t.TargetValue == 100);
    }

    [Fact]
    public void SetKpiTargetsValidator_RequiresAllTwelveMonthlyTargets()
    {
        var validator = new SetKpiTargetsValidator();
        var request = new SetKpiTargetsCommandRequest
        {
            Dto = new SetKpiTargetsDto
            {
                KpiId = Guid.NewGuid(),
                FiscalYearId = Guid.NewGuid(),
                MonthlyTargets = new List<decimal> { 1, 2, 3 },
                AnnualTarget = 100
            }
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("12 months", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetKpiTargetsValidator_RequiresAnnualTarget()
    {
        var validator = new SetKpiTargetsValidator();
        var request = new SetKpiTargetsCommandRequest
        {
            Dto = new SetKpiTargetsDto
            {
                KpiId = Guid.NewGuid(),
                FiscalYearId = Guid.NewGuid(),
                MonthlyTargets = Enumerable.Range(1, 12).Select(m => (decimal)m).ToList(),
                AnnualTarget = null
            }
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Annual target", StringComparison.OrdinalIgnoreCase));
    }
}
