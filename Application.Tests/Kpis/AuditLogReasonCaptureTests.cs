using Application.CQRS.KpiActuals.Handlers.Commands;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.DTOS.KpiActual;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class AuditLogReasonCaptureTests
{
    [Fact]
    public async Task OverrideReason_IsStampedOnAppendOnlyAuditEntry()
    {
        var auditContext = new AuditContext();
        await using var lease = await TestDbContextFactory.CreateAsync(
            new TestCurrentUserService { UserId = SeededAdminUserId, UserName = "admin" },
            auditContext);
        var (kpiId, periodId) = await SeedAsync(lease);
        var handler = CreateEnterHandler(lease, auditContext);

        var response = await handler.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = kpiId,
                FiscalPeriodId = periodId,
                Value = 42.5m,
                OverrideReason = "Approved year-end correction."
            }
        }, CancellationToken.None);

        Assert.True(response.Success);

        var entry = await lease.Context.AuditLogEntries
            .SingleAsync(a => a.EntityType == nameof(KpiActualValue));

        Assert.Equal(AuditAction.Create, entry.Action);

        var actualValue = await lease.Context.KpiActualValues.SingleAsync();
        Assert.Equal(actualValue.Id.ToString(), entry.EntityId);
        Assert.Equal(SeededAdminUserId, entry.PerformedBy);
        Assert.Equal("admin", entry.PerformedByName);
        Assert.NotEqual(default, entry.PerformedAt);
        Assert.Equal("Approved year-end correction.", entry.Reason);

        // The snapshot must carry the KPI, the period and the corrected value so the
        // append-only record is fully attributable without touching the source table.
        var newValues = entry.NewValues ?? string.Empty;
        Assert.Contains(kpiId.ToString(), newValues);
        Assert.Contains(periodId.ToString(), newValues);
        Assert.Contains("42.5", newValues);
    }

    [Fact]
    public async Task NoOverrideReason_LeavesAuditReasonNull()
    {
        var auditContext = new AuditContext();
        await using var lease = await TestDbContextFactory.CreateAsync(null, auditContext);
        var (kpiId, periodId) = await SeedAsync(lease);
        var handler = CreateEnterHandler(lease, auditContext);

        await handler.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = kpiId, FiscalPeriodId = periodId, Value = 10 }
        }, CancellationToken.None);

        var entry = await lease.Context.AuditLogEntries
            .SingleAsync(a => a.EntityType == nameof(KpiActualValue));

        Assert.Null(entry.Reason);
    }

    [Fact]
    public async Task ReasonIsCleared_ForSubsequentAuditedSaveInSameScope()
    {
        var auditContext = new AuditContext();
        await using var lease = await TestDbContextFactory.CreateAsync(null, auditContext);
        var (kpiId, periodId) = await SeedAsync(lease);
        var handler = CreateEnterHandler(lease, auditContext);

        await handler.Handle(new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = kpiId,
                FiscalPeriodId = periodId,
                Value = 10,
                OverrideReason = "Reason must not leak."
            }
        }, CancellationToken.None);

        // An unrelated save in the same scope must produce a reason-free audit entry.
        var scorecard = await lease.Context.Scorecards.FirstAsync();
        var otherGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Unrelated", Weight = 1 };
        lease.Context.Goals.Add(otherGoal);
        await lease.Context.SaveChangesAsync();

        var actualEntry = await lease.Context.AuditLogEntries
            .SingleAsync(a => a.EntityType == nameof(KpiActualValue));
        var goalEntry = await lease.Context.AuditLogEntries
            .SingleAsync(a => a.EntityType == nameof(Goal) && a.EntityId == otherGoal.Id.ToString());

        Assert.Equal("Reason must not leak.", actualEntry.Reason);
        Assert.Null(goalEntry.Reason);
    }

    private static readonly Guid SeededAdminUserId = new("11111111-1111-1111-1111-111111111111");

    private static EnterActualValueCommandHandler CreateEnterHandler(
        TestDbContextLease lease,
        IAuditContext auditContext)
    {
        return new EnterActualValueCommandHandler(
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new TestCurrentUserService { UserId = SeededAdminUserId, UserName = "admin" },
            new TestSpmsJobScheduler(),
            auditContext);
    }

    private static async Task<(Guid KpiId, Guid PeriodId)> SeedAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2099);
        var periods = calendar.GeneratePeriods(fiscalYear);

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
        var admin = new User
        {
            Id = SeededAdminUserId,
            Username = "admin",
            FullName = "Admin",
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.Users.Add(admin);
        await lease.Context.SaveChangesAsync();

        return (kpi.Id, periods[0].Id);
    }
}