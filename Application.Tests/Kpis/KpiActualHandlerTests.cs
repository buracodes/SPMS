using Application.CQRS.KpiActuals.Handlers.Commands;
using Application.CQRS.KpiActuals.Handlers.Queries;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Jobs;
using Application.Services;
using Application.Tests.TestSupport;
using Application.Validation.KpiActual;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiActualHandlerTests
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

    private static (EnterActualValueCommandHandler enter, GetActualValuesQueryHandler get, GetActualValueHistoryQueryHandler history) CreateHandlers(TestDbContextLease lease, ICurrentUserService? currentUserService = null)
    {
        var actualRepo = new KpiActualValueRepository(lease.Context);
        return (
            new EnterActualValueCommandHandler(
                new KpiRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                actualRepo,
                currentUserService ?? new TestCurrentUserService(),
                new TestSpmsJobScheduler(),
                new AuditContext()),
            new GetActualValuesQueryHandler(
                new KpiRepository(lease.Context),
                new FiscalYearRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                actualRepo,
                currentUserService ?? new TestCurrentUserService()),
            new GetActualValueHistoryQueryHandler(
                new KpiRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                actualRepo,
                currentUserService ?? new TestCurrentUserService()));
    }

    [Fact]
    public async Task EnterActualValue_CreatesAppendOnlyRecord()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, _, _) = CreateHandlers(lease);
        var response = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 42.5m, Note = "First entry" }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(42.5m, response.Data!.Value);
        Assert.Equal(KpiDataSource.Manual, response.Data.DataSource);
        Assert.Equal(period.FiscalMonth, response.Data.FiscalMonth);
        Assert.Equal(1, await lease.Context.KpiActualValues.CountAsync());
    }

    [Fact]
    public async Task EnterActualValue_EnqueuesScoreRecalculationJob_WhenKpiHasKra()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var scheduler = new TestSpmsJobScheduler();
        var actualRepo = new KpiActualValueRepository(lease.Context);
        var enter = new EnterActualValueCommandHandler(
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            actualRepo,
            new TestCurrentUserService(),
            scheduler,
            new AuditContext());

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 42.5m }
        }, CancellationToken.None);

        Assert.Contains(nameof(IScoreSnapshotRecalculationJob), scheduler.EnqueuedJobIds);
    }

    [Fact]
    public async Task EnterActualValue_LatestWriteWins_SameSource()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, get, _) = CreateHandlers(lease);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 10 }
        }, CancellationToken.None);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 15 }
        }, CancellationToken.None);

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal(15, response.Data!.Values.Single().Value);

        // History preserves both revisions (append-only).
        var values = await lease.Context.KpiActualValues.ToListAsync();
        Assert.Equal(2, values.Count);

        var latest = await lease.Context.KpiActualValues
            .OrderByDescending(v => v.AsOfDate)
            .FirstAsync();
        Assert.Equal(15, latest.Value);
    }

    [Fact]
    public async Task GetActualValues_ReturnsLatestPerPeriodPerSource()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, get, _) = CreateHandlers(lease);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 10 }
        }, CancellationToken.None);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 12, DataSource = KpiDataSource.Automated }
        }, CancellationToken.None);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 11 }
        }, CancellationToken.None);

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);

        Assert.True(response.Success);
        // Latest write per source: manual = 11, automated = 12.
        Assert.Equal(2, response.Data!.Values.Count);
        Assert.Equal(11, response.Data.Values.Single(v => v.DataSource == KpiDataSource.Manual).Value);
        Assert.Equal(12, response.Data.Values.Single(v => v.DataSource == KpiDataSource.Automated).Value);
    }

    [Fact]
    public async Task GetActualValues_ReturnsAllPeriodsInOrder()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var (enter, get, _) = CreateHandlers(lease);
        var months = new[] { 1, 3, 12 };
        var periods = await lease.Context.FiscalPeriods.Where(p => months.Contains(p.FiscalMonth)).ToListAsync();

        foreach (var period in periods)
        {
            await enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = period.FiscalMonth * 10 }
            }, CancellationToken.None);
        }

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);

        Assert.Equal(3, response.Data!.Values.Count);
        Assert.Equal(new[] { 1, 3, 12 }, response.Data.Values.Select(v => v.FiscalMonth).ToArray());
    }

    [Fact]
    public async Task EnterActualValue_UnknownKpi_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, _, _) = CreateHandlers(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = Guid.NewGuid(), FiscalPeriodId = period.Id, Value = 1 }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task EnterActualValue_UnknownPeriod_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);

        var (enter, _, _) = CreateHandlers(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = Guid.NewGuid(), Value = 1 }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task EnterActualValue_PeriodFromOtherFiscalYear_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var otherYear = new FiscalCalendarService().CreateFiscalYear(2026);
        var otherPeriods = new FiscalCalendarService().GeneratePeriods(otherYear);
        lease.Context.FiscalYears.Add(otherYear);
        lease.Context.FiscalPeriods.AddRange(otherPeriods);
        await lease.Context.SaveChangesAsync();

        var otherPeriod = otherPeriods.First();
        var (enter, _, _) = CreateHandlers(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = otherPeriod.Id, Value = 1 }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task GetActualValueHistory_ReturnsAllRevisionsDescending()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, _, history) = CreateHandlers(lease);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 10 }
        }, CancellationToken.None);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 20, DataSource = KpiDataSource.Automated }
        }, CancellationToken.None);

        var response = await history.Handle(new GetActualValueHistoryQueryRequest { KpiId = kpiId, FiscalPeriodId = period.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.Revisions.Count);
        Assert.Equal(new[] { 20m, 10m }, response.Data.Revisions.Select(r => r.Value).ToArray());
        Assert.True(response.Data.Revisions[0].AsOfDate >= response.Data.Revisions[1].AsOfDate);
    }

    [Fact]
    public async Task GetActualValueHistory_UnknownKpi_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (_, _, history) = CreateHandlers(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            history.Handle(new GetActualValueHistoryQueryRequest { KpiId = Guid.NewGuid(), FiscalPeriodId = period.Id }, CancellationToken.None));
    }

    [Fact]
    public void EnterActualValueValidator_RejectsCalculatedSource()
    {
        var validator = new EnterActualValueValidator();
        var request = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = Guid.NewGuid(),
                FiscalPeriodId = Guid.NewGuid(),
                Value = 10,
                DataSource = KpiDataSource.Calculated
            }
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("calculated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnterActualValueValidator_RequiresKpiAndPeriod()
    {
        var validator = new EnterActualValueValidator();
        var request = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { Value = 10 }
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("KPI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("period", StringComparison.OrdinalIgnoreCase));
    }

    // ── FR-305 division read scoping ────────────────────────────────────────

    [Fact]
    public async Task GetActualValues_DivisionAccountForeignKpi_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var ownedBy = new Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };
        var caller = new Division { Id = Guid.NewGuid(), Name = "Sales", Code = "SAL" };
        lease.Context.Divisions.AddRange(ownedBy, caller);
        var kpi = await lease.Context.Kpis.FirstAsync(k => k.Id == kpiId);
        kpi.DivisionOwnerId = ownedBy.Id;
        await lease.Context.SaveChangesAsync();

        var divisionUser = new TestCurrentUserService { DivisionId = caller.Id };
        var (_, get, _) = CreateHandlers(lease, divisionUser);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None));
    }

    [Fact]
    public async Task GetActualValues_DivisionAccountOwnKpi_SucceedsWithoutValues()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var ownedBy = new Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };
        lease.Context.Divisions.Add(ownedBy);
        var kpi = await lease.Context.Kpis.FirstAsync(k => k.Id == kpiId);
        kpi.DivisionOwnerId = ownedBy.Id;
        await lease.Context.SaveChangesAsync();

        var divisionUser = new TestCurrentUserService { DivisionId = ownedBy.Id };
        var (_, get, _) = CreateHandlers(lease, divisionUser);

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);
        Assert.True(response.Success);
        Assert.Empty(response.Data!.Values);
    }

    [Fact]
    public async Task GetActualValues_UserOwnedKpiVisibleToOwner()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);

        var owner = Guid.NewGuid();
        var callerDivision = new Division { Id = Guid.NewGuid(), Name = "Sales", Code = "SAL" };
        lease.Context.Divisions.Add(callerDivision);
        lease.Context.Users.Add(new User { Id = owner, Username = "kpi.owner", FullName = "Kpi Owner", DivisionId = callerDivision.Id });
        var kpi = await lease.Context.Kpis.FirstAsync(k => k.Id == kpiId);
        kpi.OwnerId = owner;
        await lease.Context.SaveChangesAsync();

        var ownerUser = new TestCurrentUserService { UserId = owner, DivisionId = callerDivision.Id };
        var (_, get, history) = CreateHandlers(lease, ownerUser);

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);
        Assert.True(response.Success);

        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();
        var historyResponse = await history.Handle(new GetActualValueHistoryQueryRequest { KpiId = kpiId, FiscalPeriodId = period.Id }, CancellationToken.None);
        Assert.True(historyResponse.Success);
    }

    [Fact]
    public async Task GetActualValueHistory_DivisionAccountForeignKpi_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var ownedBy = new Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };
        lease.Context.Divisions.Add(ownedBy);
        var kpi = await lease.Context.Kpis.FirstAsync(k => k.Id == kpiId);
        kpi.DivisionOwnerId = ownedBy.Id;
        await lease.Context.SaveChangesAsync();

        var divisionUser = new TestCurrentUserService { DivisionId = Guid.NewGuid() };
        var (_, _, history) = CreateHandlers(lease, divisionUser);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            history.Handle(new GetActualValueHistoryQueryRequest { KpiId = kpiId, FiscalPeriodId = period.Id }, CancellationToken.None));
    }
}
