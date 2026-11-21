using Application.CQRS.KpiActuals.Handlers.Commands;
using Application.CQRS.KpiActuals.Handlers.Queries;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IService;
using Application.Jobs;
using Application.Services;
using Application.Tests.TestSupport;
using Application.Validation.KpiActual;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
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

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard" };
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

    private static (EnterActualValueCommandHandler enter, GetActualValuesQueryHandler get, GetActualValueHistoryQueryHandler history) CreateHandlers(
        TestDbContextLease lease,
        ICurrentUserService? currentUserService = null,
        IEffectivePermissionService? effectivePermissionService = null)
    {
        var user = currentUserService ?? new TestCurrentUserService();
        var effective = effectivePermissionService ?? new TestEffectivePermissionService();
        var organizationalScope = new OrganizationalScopeService(user, effective);
        var actualRepo = new KpiActualValueRepository(lease.Context);
        var userRepo = new UserRepository(lease.Context);
        var unitOfWork = new SpmsUnitOfWork(lease.Context);
        return (
            new EnterActualValueCommandHandler(
                new KpiRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                actualRepo,
                userRepo,
                user,
                new TestSpmsJobScheduler(),
                new AuditContext(),
                effective,
                unitOfWork),
            new GetActualValuesQueryHandler(
                new KpiRepository(lease.Context),
                new FiscalYearRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                actualRepo,
                userRepo,
                user,
                organizationalScope),
            new GetActualValueHistoryQueryHandler(
                new KpiRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                actualRepo,
                userRepo,
                new AuditLogEntryRepository(lease.Context),
                user,
                organizationalScope));
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
    public async Task EnterActualValue_AttributionComesFromAuthenticatedPrincipal()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var user = new User { Id = Guid.NewGuid(), Username = "jdoe", FullName = "Jane Doe" };
        lease.Context.Users.Add(user);
        await lease.Context.SaveChangesAsync();

        var principal = new TestCurrentUserService { UserId = user.Id, UserName = "login-name" };
        var (enter, get, _) = CreateHandlers(lease, principal);

        var response = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 42.5m }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(user.Id, response.Data!.RecordedBy);
        Assert.Equal("Jane Doe", response.Data.EnteredByName);

        var stored = Assert.Single(await lease.Context.KpiActualValues.ToListAsync());
        Assert.Equal(user.Id, stored.RecordedBy);

        var read = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = period.FiscalYearId }, CancellationToken.None);
        Assert.Equal("Jane Doe", Assert.Single(read.Data!.Values).EnteredByName);
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
            new UserRepository(lease.Context),
            new TestCurrentUserService(),
            scheduler,
            new AuditContext(),
            new TestEffectivePermissionService(),
            new SpmsUnitOfWork(lease.Context));

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 42.5m }
        }, CancellationToken.None);

        Assert.Contains(nameof(IScoreSnapshotRecalculationJob), scheduler.EnqueuedJobIds);
    }

    [Fact]
    public async Task EnterActualValue_SupersedesPreviousRevision_AppendOnly()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, get, _) = CreateHandlers(lease);

        var firstResponse = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 10 }
        }, CancellationToken.None);

        var secondResponse = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 15 }
        }, CancellationToken.None);

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal(15, response.Data!.Values.Single().Value);

        // History preserves both revisions (append-only), with an explicit forward link.
        var values = await lease.Context.KpiActualValues.ToListAsync();
        Assert.Equal(2, values.Count);
        var previous = Assert.Single(values, v => v.Id == firstResponse.Data!.Id);
        var replacement = Assert.Single(values, v => v.Id == secondResponse.Data!.Id);
        Assert.False(previous.IsCurrent);
        Assert.True(replacement.IsCurrent);
        Assert.Equal(previous.Id, replacement.SupersedesActualId);
        Assert.All(values, v => Assert.False(v.IsDeleted));
    }

    [Fact]
    public async Task GetActualValues_ReturnsOneManualCurrentValuePerPeriod()
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
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 12 }
        }, CancellationToken.None);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 11 }
        }, CancellationToken.None);

        var response = await get.Handle(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId }, CancellationToken.None);

        Assert.True(response.Success);
        // Exactly one current revision per KPI + period, and HTTP entry is always Manual.
        var current = Assert.Single(response.Data!.Values);
        Assert.Equal(11, current.Value);
        Assert.Equal(KpiDataSource.Manual, current.DataSource);
        Assert.Equal(1, await lease.Context.KpiActualValues.CountAsync(v => v.IsCurrent));
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
    public async Task EnterActualValue_InactiveKpi_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var kpi = await lease.Context.Kpis.SingleAsync(k => k.Id == kpiId);
        kpi.IsActive = false;
        await lease.Context.SaveChangesAsync();

        var (enter, _, _) = CreateHandlers(lease);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 10 }
            }, CancellationToken.None));

        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await lease.Context.KpiActualValues.CountAsync());
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
        var (_, scorecardId, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, _, history) = CreateHandlers(lease);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 10 }
        }, CancellationToken.None);

        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 20 }
        }, CancellationToken.None);

        var response = await history.Handle(new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = kpiId, FiscalPeriodId = period.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.Revisions.Count);
        Assert.Equal(new[] { 20m, 10m }, response.Data.Revisions.Select(r => r.Value).ToArray());
        Assert.True(response.Data.Revisions[0].AsOfDate >= response.Data.Revisions[1].AsOfDate);
    }

    [Fact]
    public async Task ThreeCorrections_KeepOnlyLatestCurrent_AndExposeTraversableHistoryChain()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, scorecardId, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (enter, get, history) = CreateHandlers(lease);
        var responses = new List<ActualValueDto>();
        foreach (var value in new[] { 10m, 20m, 30m })
        {
            var response = await enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = value }
            }, CancellationToken.None);

            Assert.True(response.Success);
            responses.Add(response.Data!);
        }

        var rows = await lease.Context.KpiActualValues.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsDeleted));

        var currentRow = Assert.Single(rows, row => row.IsCurrent);
        Assert.Equal(responses[2].Id, currentRow.Id);
        Assert.Equal(responses[1].Id, currentRow.SupersedesActualId);

        var middleRow = Assert.Single(rows, row => row.Id == responses[1].Id);
        Assert.False(middleRow.IsCurrent);
        Assert.Equal(responses[0].Id, middleRow.SupersedesActualId);

        var firstRow = Assert.Single(rows, row => row.Id == responses[0].Id);
        Assert.False(firstRow.IsCurrent);
        Assert.Null(firstRow.SupersedesActualId);

        var currentResponse = await get.Handle(
            new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId },
            CancellationToken.None);
        Assert.Equal(30m, Assert.Single(currentResponse.Data!.Values).Value);

        var historyResponse = await history.Handle(
            new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = kpiId, FiscalPeriodId = period.Id },
            CancellationToken.None);
        Assert.Equal(3, historyResponse.Data!.Revisions.Count);
        Assert.Equal(
            new[] { responses[2].Id, responses[1].Id, responses[0].Id },
            historyResponse.Data.Revisions.Select(r => r.Id));
        Assert.Equal(
            new[] { 30m, 20m, 10m },
            historyResponse.Data.Revisions.Select(r => r.Value));
        Assert.True(historyResponse.Data.Revisions[0].IsCurrent);
        Assert.Equal(responses[1].Id, historyResponse.Data.Revisions[0].SupersedesActualId);
        Assert.Equal(responses[0].Id, historyResponse.Data.Revisions[1].SupersedesActualId);
        Assert.Null(historyResponse.Data.Revisions[2].SupersedesActualId);
    }

    [Fact]
    public async Task GetActualValueHistory_UnknownKpi_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, scorecardId, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var (_, _, history) = CreateHandlers(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            history.Handle(new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = Guid.NewGuid(), FiscalPeriodId = period.Id }, CancellationToken.None));
    }

    [Fact]
    public void EnterActualValueValidator_DoesNotAcceptClientDataSource()
    {
        Assert.Null(typeof(EnterActualValueDto).GetProperty("DataSource"));

        var validator = new EnterActualValueValidator();
        var request = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = Guid.NewGuid(),
                FiscalPeriodId = Guid.NewGuid(),
                Value = 10
            }
        };

        Assert.True(validator.Validate(request).IsValid);
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

    // â”€â”€ FR-305 division read scoping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        var (fiscalYearId, scorecardId, _, kpiId) = CreateFixture(lease);

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
        var historyResponse = await history.Handle(new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = kpiId, FiscalPeriodId = period.Id }, CancellationToken.None);
        Assert.True(historyResponse.Success);
    }

    [Fact]
    public async Task GetActualValueHistory_DivisionAccountForeignKpi_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, scorecardId, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var ownedBy = new Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };
        lease.Context.Divisions.Add(ownedBy);
        var kpi = await lease.Context.Kpis.FirstAsync(k => k.Id == kpiId);
        kpi.DivisionOwnerId = ownedBy.Id;
        await lease.Context.SaveChangesAsync();

        var divisionUser = new TestCurrentUserService { DivisionId = Guid.NewGuid() };
        var (_, _, history) = CreateHandlers(lease, divisionUser);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            history.Handle(new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = kpiId, FiscalPeriodId = period.Id }, CancellationToken.None));
    }

    // â”€â”€ Organization-wide authority precedes division ownership â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly string[] AdminLivePermissions =
    {
        Permissions.PerformanceMonitorView, Permissions.ScorecardView, Permissions.ScorecardCreate,
        Permissions.ScorecardEdit, Permissions.ScorecardClose, Permissions.ActualsView,
        Permissions.ActualsEnter, Permissions.ActualsOverride, Permissions.DivisionView,
        Permissions.DivisionManage, Permissions.DivisionPlan, Permissions.DivisionActualsEnter
    };

    private static readonly string[] PowerUserLivePermissions =
    {
        Permissions.PerformanceMonitorView, Permissions.ScorecardView, Permissions.ScorecardCreate,
        Permissions.ScorecardEdit, Permissions.ActualsView, Permissions.ActualsEnter,
        Permissions.ActualsOverride, Permissions.DivisionView, Permissions.DivisionPlan,
        Permissions.DivisionActualsEnter
    };

    private static readonly string[] ExecutiveLivePermissions =
    {
        Permissions.PerformanceMonitorView, Permissions.ScorecardView, Permissions.ActualsView,
        Permissions.DivisionView, Permissions.ReportsView
    };

    private static readonly string[] DataInputLivePermissions =
    {
        Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DivisionActualsEnter,
        Permissions.DataUpload, Permissions.DataValidate
    };

    private static readonly string[] ViewOnlyLivePermissions =
    {
        Permissions.ScorecardView, Permissions.ActualsView, Permissions.DivisionView,
        Permissions.ReportsView
    };

    private static TestEffectivePermissionService LiveGrants(Guid userId, params string[] permissions)
    {
        var service = new TestEffectivePermissionService();
        foreach (var permission in permissions)
        {
            service.Grant(userId, permission);
        }

        return service;
    }

    private static TestCurrentUserService DivisionClaimedUser(
        Guid userId, Guid divisionId, params string[] jwtPermissions)
        => new()
        {
            UserId = userId,
            DivisionId = divisionId,
            Permissions = jwtPermissions
        };

    /// <summary>
    /// A KPI on the corporate scorecard owned by a division the caller does not belong to.
    /// </summary>
    private static async Task<(Guid fiscalYearId, Guid scorecardId, Guid kpiId, Guid periodId)> MakeForeignDivisionKpiAsync(
        TestDbContextLease lease, Guid recordedBy)
    {
        var (fiscalYearId, scorecardId, _, kpiId) = CreateFixture(lease);

        var ownerDivision = new Division { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };
        lease.Context.Divisions.Add(ownerDivision);
        lease.Context.Users.Add(new User
        {
            Id = recordedBy,
            Username = $"claimed.{recordedBy:N}",
            FullName = "Division Claimed User"
        });

        var kpi = await lease.Context.Kpis.SingleAsync(k => k.Id == kpiId);
        kpi.DivisionOwnerId = ownerDivision.Id;
        await lease.Context.SaveChangesAsync();

        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();
        return (fiscalYearId, scorecardId, kpiId, period.Id);
    }

    private static async Task AssertForeignDivisionReadAllowedAsync(string[] livePermissions)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        var (fiscalYearId, scorecardId, kpiId, periodId) = await MakeForeignDivisionKpiAsync(lease, userId);

        var user = DivisionClaimedUser(userId, Guid.NewGuid(), livePermissions);
        var live = LiveGrants(userId, livePermissions);
        var (_, get, history) = CreateHandlers(lease, user, live);

        var response = await get.Handle(
            new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId },
            CancellationToken.None);
        Assert.True(response.Success);

        var historyResponse = await history.Handle(
            new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = kpiId, FiscalPeriodId = periodId },
            CancellationToken.None);
        Assert.True(historyResponse.Success);
    }

    private static async Task AssertForeignDivisionReadForbiddenAsync(string[] livePermissions)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        var (fiscalYearId, scorecardId, kpiId, periodId) = await MakeForeignDivisionKpiAsync(lease, userId);

        var user = DivisionClaimedUser(userId, Guid.NewGuid(), livePermissions);
        var live = LiveGrants(userId, livePermissions);
        var (_, get, history) = CreateHandlers(lease, user, live);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            get.Handle(
                new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId },
                CancellationToken.None));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            history.Handle(
                new GetActualValueHistoryQueryRequest { ScorecardId = scorecardId, KpiId = kpiId, FiscalPeriodId = periodId },
                CancellationToken.None));
    }

    private static async Task AssertForeignDivisionWriteAsync(string[] livePermissions, bool allowed)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        var (_, scorecardId, kpiId, periodId) = await MakeForeignDivisionKpiAsync(lease, userId);

        var user = DivisionClaimedUser(userId, Guid.NewGuid(), livePermissions);
        var live = LiveGrants(userId, livePermissions);
        var (enter, _, _) = CreateHandlers(lease, user, live);

        var request = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = periodId, Value = 12 }
        };

        if (allowed)
        {
            var response = await enter.Handle(request, CancellationToken.None);
            Assert.True(response.Success);
            Assert.Equal(1, await lease.Context.KpiActualValues.CountAsync());
            return;
        }

        await Assert.ThrowsAsync<ForbiddenException>(() => enter.Handle(request, CancellationToken.None));
        Assert.Equal(0, await lease.Context.KpiActualValues.CountAsync());
    }

    [Fact]
    public Task GetActualValues_AdminWithDivisionClaim_ReadsForeignDivisionKpi()
        => AssertForeignDivisionReadAllowedAsync(AdminLivePermissions);

    [Fact]
    public Task GetActualValues_PowerUserWithDivisionClaim_ReadsForeignDivisionKpi()
        => AssertForeignDivisionReadAllowedAsync(PowerUserLivePermissions);

    [Fact]
    public Task GetActualValues_ExecutiveWithDivisionClaim_ReadsForeignDivisionKpi()
        => AssertForeignDivisionReadAllowedAsync(ExecutiveLivePermissions);

    [Fact]
    public Task GetActualValues_DataInputWithDivisionClaim_ForeignDivisionKpi_ThrowsForbidden()
        => AssertForeignDivisionReadForbiddenAsync(DataInputLivePermissions);

    [Fact]
    public Task GetActualValues_ViewOnlyWithDivisionClaim_ForeignDivisionKpi_ThrowsForbidden()
        => AssertForeignDivisionReadForbiddenAsync(ViewOnlyLivePermissions);

    [Fact]
    public async Task GetActualValues_RevokedMonitoringGrant_WithStaleJwtClaim_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var userId = Guid.NewGuid();
        var (fiscalYearId, scorecardId, kpiId, _) = await MakeForeignDivisionKpiAsync(lease, userId);

        // The JWT still carries performance:monitor.view, but the live grant was revoked.
        var user = DivisionClaimedUser(
            userId,
            Guid.NewGuid(),
            Permissions.PerformanceMonitorView,
            Permissions.ActualsView,
            Permissions.ActualsOverride);
        var live = LiveGrants(userId, Permissions.ActualsView);
        var (_, get, _) = CreateHandlers(lease, user, live);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            get.Handle(
                new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId },
                CancellationToken.None));
    }

    [Fact]
    public Task EnterActualValue_PowerUserWithDivisionClaim_ForeignKpi_WithLiveOverride_Succeeds()
        => AssertForeignDivisionWriteAsync(PowerUserLivePermissions, allowed: true);

    [Fact]
    public Task EnterActualValue_AdminWithDivisionClaim_ForeignKpi_WithLiveOverride_Succeeds()
        => AssertForeignDivisionWriteAsync(AdminLivePermissions, allowed: true);

    [Fact]
    public Task EnterActualValue_DataInputWithDivisionClaim_ForeignKpi_ThrowsForbidden()
        => AssertForeignDivisionWriteAsync(DataInputLivePermissions, allowed: false);

    [Fact]
    public Task EnterActualValue_ExecutiveWithDivisionClaim_ForeignKpi_ThrowsForbidden()
        => AssertForeignDivisionWriteAsync(ExecutiveLivePermissions, allowed: false);

    [Fact]
    public Task EnterActualValue_ViewOnlyWithDivisionClaim_ForeignKpi_ThrowsForbidden()
        => AssertForeignDivisionWriteAsync(ViewOnlyLivePermissions, allowed: false);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EnterActualValue_FailureInEitherSaveRollsBackWholeRevisionChange(int failOnSaveCall)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, _, kpiId) = CreateFixture(lease);
        var period = await lease.Context.FiscalPeriods.OrderBy(p => p.FiscalMonth).FirstAsync();

        var priorId = Guid.NewGuid();
        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            Id = priorId,
            KpiId = kpiId,
            FiscalPeriodId = period.Id,
            Value = 10,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow.AddMinutes(-5),
            IsCurrent = true
        });
        await lease.Context.SaveChangesAsync();
        var auditCountBeforeCorrection = await lease.Context.AuditLogEntries.CountAsync();

        var actualRepo = new KpiActualValueRepository(lease.Context);
        var enter = new EnterActualValueCommandHandler(
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            actualRepo,
            new UserRepository(lease.Context),
            new TestCurrentUserService(),
            new TestSpmsJobScheduler(),
            new AuditContext(),
            new TestEffectivePermissionService(),
            new FailingUnitOfWork(new SpmsUnitOfWork(lease.Context), failOnSaveCall));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            enter.Handle(new EnterActualValueCommandRequest
            {
                Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = period.Id, Value = 15 }
            }, CancellationToken.None));

        lease.Context.ChangeTracker.Clear();
        var rows = await lease.Context.KpiActualValues.AsNoTracking().ToListAsync();
        var persisted = Assert.Single(rows);
        Assert.Equal(priorId, persisted.Id);
        Assert.True(persisted.IsCurrent);
        Assert.Null(persisted.SupersedesActualId);
        Assert.Equal(10, persisted.Value);
        Assert.Equal(auditCountBeforeCorrection, await lease.Context.AuditLogEntries.CountAsync());
    }

    /// <summary>
    /// Fails either save in the retire + insert sequence so the test can prove the two saves
    /// commit atomically. The real unit of work still owns the transaction and rollback.
    /// </summary>
    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        private readonly IUnitOfWork inner;
        private readonly int failOnSaveCall;
        private int saveCalls;

        public FailingUnitOfWork(IUnitOfWork inner, int failOnSaveCall)
        {
            this.inner = inner;
            this.failOnSaveCall = failOnSaveCall;
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
            => inner.ExecuteInTransactionAsync(work, cancellationToken);

        public Task AddRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            where TEntity : class
            => inner.AddRangeAsync(entities, cancellationToken);

        public Task RemoveRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            where TEntity : class
            => inner.RemoveRangeAsync(entities, cancellationToken);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (++saveCalls == failOnSaveCall)
            {
                throw new InvalidOperationException($"Injected failure during save {failOnSaveCall}.");
            }

            return inner.SaveChangesAsync(cancellationToken);
        }
    }
}
