using API.Controllers;
using Application.CQRS.Division.Handlers.Queries;
using Application.CQRS.Division.Requests.Queries;
using Application.CQRS.KpiActuals;
using Application.CQRS.KpiActuals.Handlers.Commands;
using Application.CQRS.KpiActuals.Handlers.Queries;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using DivisionEntity = Domain.Entities.Division;

namespace Application.Tests.Kpis;

public class ActualHistoryHandlerTests
{
    private static readonly Guid RecorderOneId = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid RecorderTwoId = new("20000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task CorporateHistory_ReturnsCompleteProjection_WithCreateAuditReasonsAndBatchedNames()
    {
        var auditContext = new AuditContext();
        var writer = new TestCurrentUserService { UserId = RecorderOneId, UserName = "alice" };
        await using var lease = await TestDbContextFactory.CreateAsync(writer, auditContext);
        var fixture = await SeedCorporateFixtureAsync(lease);
        SeedUsers(lease);

        var enter = CreateEnterHandler(lease, auditContext, writer);
        var first = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 10,
                Note = "Original",
                // Even the original revision's audit row has a reason; history must still
                // leave its CorrectionReason null because it supersedes nothing.
                OverrideReason = "Initial audit reason, not a correction."
            }
        }, CancellationToken.None);

        var secondWriter = new TestCurrentUserService { UserId = RecorderTwoId, UserName = "bob" };
        var secondEnter = CreateEnterHandler(lease, auditContext, secondWriter);
        var second = await secondEnter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 25,
                Note = "Corrected",
                OverrideReason = "Corrected transposed digits."
            }
        }, CancellationToken.None);

        // The second write uses a different authenticated principal; the repository still
        // resolves both display names in the history query's batched lookup.
        Assert.NotEqual(first.Data!.RecordedBy, second.Data!.RecordedBy);

        var handler = CreateCorporateHistoryHandler(lease, writer);
        var response = await handler.Handle(new GetActualValueHistoryQueryRequest
        {
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId
        }, CancellationToken.None);

        Assert.True(response.Success);
        var history = response.Data!;
        Assert.Equal(fixture.ScorecardId, history.ScorecardId);
        Assert.Equal(fixture.KpiId, history.KpiId);
        Assert.Equal("Corporate KPI", history.KpiName);
        Assert.Equal(fixture.PeriodId, history.FiscalPeriodId);
        Assert.Equal(fixture.FiscalMonth, history.FiscalMonth);
        Assert.Equal(fixture.Quarter, history.Quarter);
        Assert.Contains($"Month {fixture.FiscalMonth}", history.FiscalPeriodLabel);
        Assert.Equal(fixture.FiscalYearId, history.FiscalYearId);
        Assert.Equal("FY Test 2025", history.FiscalYearLabel);
        Assert.False(history.HasChainIntegrityIssues);

        var revisions = history.Revisions;
        Assert.Equal(2, revisions.Count);
        Assert.Equal(second.Data!.Id, revisions[0].Id);
        Assert.Equal(first.Data!.Id, revisions[1].Id);
        Assert.Equal(new[] { 2, 1 }, revisions.Select(r => r.RevisionNumber));
        Assert.Equal(new[] { 25m, 10m }, revisions.Select(r => r.Value));
        Assert.True(revisions[0].IsCurrent);
        Assert.False(revisions[1].IsCurrent);
        Assert.Equal(first.Data.Id, revisions[0].SupersedesActualId);
        Assert.Null(revisions[1].SupersedesActualId);
        Assert.Equal("Corrected transposed digits.", revisions[0].CorrectionReason);
        Assert.Null(revisions[1].CorrectionReason);

        Assert.All(revisions, r =>
        {
            Assert.Equal("Corporate KPI", r.KpiName);
            Assert.Equal(KpiDataSource.Manual, r.DataSource);
            Assert.Equal(fixture.PeriodId, r.FiscalPeriodId);
            Assert.Equal(fixture.FiscalMonth, r.FiscalMonth);
            Assert.Equal(fixture.Quarter, r.Quarter);
            Assert.Equal(fixture.FiscalYearId, r.FiscalYearId);
            Assert.Equal("FY Test 2025", r.FiscalYearLabel);
            Assert.NotEqual(default, r.AsOfDate);
            Assert.NotEqual(default, r.CreatedDate);
        });
        Assert.Equal(new[] { "Bob Recorder", "Alice Recorder" }, revisions.Select(r => r.EnteredByName));
        Assert.Equal(RecorderTwoId, revisions[0].RecordedBy);
        Assert.Equal(RecorderOneId, revisions[1].RecordedBy);
    }

    [Fact]
    public async Task CorporateHistory_UsesOnlyCreateAuditRowForThisRevision()
    {
        var auditContext = new AuditContext();
        var writer = new TestCurrentUserService { UserId = RecorderOneId, UserName = "alice" };
        await using var lease = await TestDbContextFactory.CreateAsync(writer, auditContext);
        var fixture = await SeedCorporateFixtureAsync(lease);
        SeedUsers(lease);

        var enter = CreateEnterHandler(lease, auditContext, writer);
        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 10 }
        }, CancellationToken.None);
        var replacement = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 20,
                OverrideReason = "Real Create-audit reason."
            }
        }, CancellationToken.None);

        var replacementId = replacement.Data!.Id;
        AddAuditDecoys(lease, replacementId);
        await lease.Context.SaveChangesAsync();

        var handler = CreateCorporateHistoryHandler(lease, writer);
        var response = await handler.Handle(new GetActualValueHistoryQueryRequest
        {
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId
        }, CancellationToken.None);

        Assert.Equal("Real Create-audit reason.", response.Data!.Revisions[0].CorrectionReason);
    }

    [Fact]
    public async Task CorporateHistory_MissingCreateAuditAndUnrelatedRows_ReturnsNullReason()
    {
        var auditContext = new AuditContext();
        var writer = new TestCurrentUserService { UserId = RecorderOneId, UserName = "alice" };
        await using var lease = await TestDbContextFactory.CreateAsync(writer, auditContext);
        var fixture = await SeedCorporateFixtureAsync(lease);
        SeedUsers(lease);

        var enter = CreateEnterHandler(lease, auditContext, writer);
        await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 10 }
        }, CancellationToken.None);
        var replacement = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 20,
                OverrideReason = "Reason that will be removed."
            }
        }, CancellationToken.None);

        var replacementId = replacement.Data!.Id;
        var createRows = await lease.Context.AuditLogEntries
            .Where(a => a.EntityId == replacementId.ToString()
                && a.EntityType == nameof(KpiActualValue)
                && a.Action == AuditAction.Create)
            .ToListAsync();
        lease.Context.AuditLogEntries.RemoveRange(createRows);
        AddAuditDecoys(lease, replacementId);
        await lease.Context.SaveChangesAsync();

        var handler = CreateCorporateHistoryHandler(lease, writer);
        var response = await handler.Handle(new GetActualValueHistoryQueryRequest
        {
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId
        }, CancellationToken.None);

        Assert.Null(response.Data!.Revisions[0].CorrectionReason);
        Assert.Null(response.Data.Revisions[1].CorrectionReason);
    }

    [Fact]
    public async Task CorporateHistory_UnknownRecorder_HasNullDisplayName()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedCorporateFixtureAsync(lease);
        var missingUserId = Guid.NewGuid();

        // The fixture intentionally seeds no User row. Project an in-memory revision so the
        // batched name lookup has to handle a recorder id that no local user resolves.
        var kpi = await new KpiRepository(lease.Context).GetByIdWithScorecardAsync(fixture.KpiId);
        var period = await lease.Context.FiscalPeriods.SingleAsync(p => p.Id == fixture.PeriodId);
        var revision = new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId,
            Value = 7,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow,
            CreatedDate = DateTime.UtcNow,
            RecordedBy = missingUserId,
            IsCurrent = true
        };

        var history = await ActualHistoryProjection.BuildAsync(
            new AuditLogEntryRepository(lease.Context),
            new UserRepository(lease.Context),
            fixture.ScorecardId,
            kpi!,
            period,
            ActualRevisionHistory.Build(new[] { revision }),
            CancellationToken.None);

        Assert.Equal(missingUserId, Assert.Single(history.Revisions).RecordedBy);
        Assert.Null(history.Revisions[0].EnteredByName);
    }

    [Fact]
    public async Task CorporateHistory_WrongScorecardOrDivisionKpi_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var corporate = await SeedCorporateFixtureAsync(lease);
        var division = await SeedDivisionFixtureAsync(lease);
        var handler = CreateCorporateHistoryHandler(lease, new TestCurrentUserService());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetActualValueHistoryQueryRequest
        {
            ScorecardId = Guid.NewGuid(),
            KpiId = corporate.KpiId,
            FiscalPeriodId = corporate.PeriodId
        }, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetActualValueHistoryQueryRequest
        {
            ScorecardId = division.ScorecardId,
            KpiId = division.KpiId,
            FiscalPeriodId = division.PeriodId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CorporateHistory_MismatchedFiscalPeriod_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedCorporateFixtureAsync(lease);
        var handler = CreateCorporateHistoryHandler(lease, new TestCurrentUserService());

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new GetActualValueHistoryQueryRequest
        {
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.OtherFiscalPeriodId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task DivisionHistory_ReturnsAllRevisionsForScopedDivision()
    {
        var auditContext = new AuditContext();
        var writer = new TestCurrentUserService { UserId = RecorderOneId, UserName = "alice" };
        await using var lease = await TestDbContextFactory.CreateAsync(writer, auditContext);
        var fixture = await SeedDivisionFixtureAsync(lease);
        SeedUsers(lease);

        var enter = CreateEnterHandler(lease, auditContext, writer);
        var first = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 40 }
        }, CancellationToken.None);
        var second = await enter.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 55,
                OverrideReason = "Division correction."
            }
        }, CancellationToken.None);

        var scopedUser = new TestCurrentUserService
        {
            UserId = RecorderTwoId,
            DivisionId = fixture.DivisionId
        };
        var handler = CreateDivisionHistoryHandler(lease, scopedUser);
        var response = await handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = fixture.DivisionId,
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(fixture.ScorecardId, response.Data!.ScorecardId);
        Assert.Equal("Division KPI", response.Data.KpiName);
        Assert.Equal(new[] { second.Data!.Id, first.Data!.Id }, response.Data.Revisions.Select(r => r.Id));
        Assert.Equal(new[] { 2, 1 }, response.Data.Revisions.Select(r => r.RevisionNumber));
        Assert.Equal("Division correction.", response.Data.Revisions[0].CorrectionReason);
        Assert.Null(response.Data.Revisions[1].CorrectionReason);
        Assert.Equal("Alice Recorder", response.Data.Revisions[0].EnteredByName);
    }

    [Fact]
    public async Task DivisionHistory_ForeignDivision_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedDivisionFixtureAsync(lease);
        var foreignUser = new TestCurrentUserService { UserId = Guid.NewGuid(), DivisionId = fixture.OtherDivisionId };
        var handler = CreateDivisionHistoryHandler(lease, foreignUser);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = fixture.DivisionId,
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task DivisionHistory_CorporateKpiWrongScorecardOrSiblingKpi_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var division = await SeedDivisionFixtureAsync(lease);
        var corporate = await SeedCorporateFixtureAsync(lease);
        var user = new TestCurrentUserService { UserId = Guid.NewGuid(), DivisionId = division.DivisionId };
        var handler = CreateDivisionHistoryHandler(lease, user);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = division.DivisionId,
            ScorecardId = corporate.ScorecardId,
            KpiId = corporate.KpiId,
            FiscalPeriodId = corporate.PeriodId
        }, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = division.DivisionId,
            ScorecardId = Guid.NewGuid(),
            KpiId = division.KpiId,
            FiscalPeriodId = division.PeriodId
        }, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = division.DivisionId,
            ScorecardId = division.ScorecardId,
            KpiId = division.SiblingKpiId,
            FiscalPeriodId = division.PeriodId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task DivisionHistory_MismatchedFiscalPeriod_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedDivisionFixtureAsync(lease);
        var user = new TestCurrentUserService { UserId = Guid.NewGuid(), DivisionId = fixture.DivisionId };
        var handler = CreateDivisionHistoryHandler(lease, user);

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = fixture.DivisionId,
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.OtherFiscalPeriodId
        }, CancellationToken.None));
    }

    [Fact]
    public void HistoryEndpoints_RequireActualsViewPolicy()
    {
        var corporatePolicy = typeof(KpiActualController)
            .GetMethod(nameof(KpiActualController.History))!
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy;
        var divisionPolicy = typeof(DivisionController)
            .GetMethod(nameof(DivisionController.History))!
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy;

        Assert.Equal(Permissions.ActualsView, corporatePolicy);
        Assert.Equal(Permissions.ActualsView, divisionPolicy);
    }

    [Fact]
    public async Task DivisionHistory_CorporateMonitorGrant_CanReadOtherDivisionScope()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedDivisionFixtureAsync(lease);
        var userId = Guid.NewGuid();
        var user = new TestCurrentUserService
        {
            UserId = userId,
            DivisionId = fixture.OtherDivisionId,
            Permissions = new[] { Permissions.PerformanceMonitorView }
        };
        var live = new TestEffectivePermissionService();
        live.Grant(userId, Permissions.PerformanceMonitorView);
        var handler = CreateDivisionHistoryHandler(lease, user, live);

        var response = await handler.Handle(new GetDivisionActualValueHistoryQueryRequest
        {
            DivisionId = fixture.DivisionId,
            ScorecardId = fixture.ScorecardId,
            KpiId = fixture.KpiId,
            FiscalPeriodId = fixture.PeriodId
        }, CancellationToken.None);

        Assert.True(response.Success);
    }

    private static EnterActualValueCommandHandler CreateEnterHandler(
        TestDbContextLease lease,
        IAuditContext auditContext,
        ICurrentUserService user)
    {
        return new EnterActualValueCommandHandler(
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new UserRepository(lease.Context),
            user,
            new TestSpmsJobScheduler(),
            auditContext,
            new TestEffectivePermissionService(),
            new SpmsUnitOfWork(lease.Context));
    }

    private static GetActualValueHistoryQueryHandler CreateCorporateHistoryHandler(
        TestDbContextLease lease,
        ICurrentUserService user,
        IEffectivePermissionService? effective = null)
    {
        effective ??= new TestEffectivePermissionService();
        return new GetActualValueHistoryQueryHandler(
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new UserRepository(lease.Context),
            new AuditLogEntryRepository(lease.Context),
            user,
            new OrganizationalScopeService(user, effective));
    }

    private static GetDivisionActualValueHistoryQueryHandler CreateDivisionHistoryHandler(
        TestDbContextLease lease,
        ICurrentUserService user,
        IEffectivePermissionService? effective = null)
    {
        effective ??= new TestEffectivePermissionService();
        return new GetDivisionActualValueHistoryQueryHandler(
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new UserRepository(lease.Context),
            new AuditLogEntryRepository(lease.Context),
            new OrganizationalScopeService(user, effective));
    }

    private static void SeedUsers(TestDbContextLease lease)
    {
        lease.Context.Users.AddRange(
            new User { Id = RecorderOneId, Username = "alice", FullName = "Alice Recorder" },
            new User { Id = RecorderTwoId, Username = "bob", FullName = "Bob Recorder" });
        lease.Context.SaveChanges();
    }

    private static void AddAuditDecoys(TestDbContextLease lease, Guid replacementId)
    {
        lease.Context.AuditLogEntries.AddRange(
            new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityType = nameof(KpiActualValue),
                EntityId = replacementId.ToString(),
                Action = AuditAction.Update,
                Reason = "Update reason must never be shown.",
                PerformedAt = DateTime.UtcNow
            },
            new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityType = "SomeOtherEntity",
                EntityId = replacementId.ToString(),
                Action = AuditAction.Create,
                Reason = "Unrelated entity reason must never be shown.",
                PerformedAt = DateTime.UtcNow
            },
            new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityType = nameof(KpiActualValue),
                EntityId = Guid.NewGuid().ToString(),
                Action = AuditAction.Create,
                Reason = "Unrelated id reason must never be shown.",
                PerformedAt = DateTime.UtcNow
            });
    }

    private static async Task<CorporateFixture> SeedCorporateFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        fiscalYear.Label = "FY Test 2025";
        var periods = calendar.GeneratePeriods(fiscalYear);
        var otherFiscalYear = calendar.CreateFiscalYear(2026);
        var otherPeriods = calendar.GeneratePeriods(otherFiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Corporate Scorecard" };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Corporate Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Corporate KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Corporate KPI",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.AddRange(fiscalYear, otherFiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.FiscalPeriods.AddRange(otherPeriods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        await lease.Context.SaveChangesAsync();

        var period = periods.OrderBy(p => p.FiscalMonth).First();
        var otherPeriod = otherPeriods.OrderBy(p => p.FiscalMonth).First();
        return new CorporateFixture(
            fiscalYear.Id,
            scorecard.Id,
            kpi.Id,
            period.Id,
            period.FiscalMonth,
            period.Quarter,
            otherPeriod.Id);
    }

    private static async Task<DivisionFixture> SeedDivisionFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2027);
        var periods = calendar.GeneratePeriods(fiscalYear);
        var otherFiscalYear = calendar.CreateFiscalYear(2028);
        var otherPeriods = calendar.GeneratePeriods(otherFiscalYear);

        var division = new DivisionEntity { Id = Guid.NewGuid(), Name = "Operations", Code = "OPS" };
        var otherDivision = new DivisionEntity { Id = Guid.NewGuid(), Name = "Sales", Code = "SAL" };
        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Division Scorecard" };
        var plan = new DivisionPlan { Id = Guid.NewGuid(), DivisionId = division.Id, ScorecardId = scorecard.Id };
        var otherPlan = new DivisionPlan { Id = Guid.NewGuid(), DivisionId = otherDivision.Id, ScorecardId = scorecard.Id };
        var parentGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Corporate Goal", Weight = 100 };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = parentGoal.Id,
            Name = "Division Goal",
            Weight = 100
        };
        var otherDivisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = otherPlan.Id,
            ParentGoalId = parentGoal.Id,
            Name = "Other Division Goal",
            Weight = 100
        };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            Name = "Division KPI",
            Unit = "#",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var siblingKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = otherDivisionGoal.Id,
            Name = "Sales KPI",
            Unit = "#",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.AddRange(fiscalYear, otherFiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.FiscalPeriods.AddRange(otherPeriods);
        lease.Context.Divisions.AddRange(division, otherDivision);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.DivisionPlans.AddRange(plan, otherPlan);
        lease.Context.Goals.Add(parentGoal);
        lease.Context.DivisionGoals.AddRange(divisionGoal, otherDivisionGoal);
        lease.Context.Kpis.AddRange(kpi, siblingKpi);
        await lease.Context.SaveChangesAsync();

        var period = periods.OrderBy(p => p.FiscalMonth).First();
        var otherPeriod = otherPeriods.OrderBy(p => p.FiscalMonth).First();
        return new DivisionFixture(
            fiscalYear.Id,
            scorecard.Id,
            division.Id,
            otherDivision.Id,
            kpi.Id,
            siblingKpi.Id,
            period.Id,
            otherPeriod.Id);
    }

    private sealed record CorporateFixture(
        Guid FiscalYearId,
        Guid ScorecardId,
        Guid KpiId,
        Guid PeriodId,
        int FiscalMonth,
        int Quarter,
        Guid OtherFiscalPeriodId);

    private sealed record DivisionFixture(
        Guid FiscalYearId,
        Guid ScorecardId,
        Guid DivisionId,
        Guid OtherDivisionId,
        Guid KpiId,
        Guid SiblingKpiId,
        Guid PeriodId,
        Guid OtherFiscalPeriodId);
}
