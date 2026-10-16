using Application.CQRS.Division.Handlers.Commands;
using Application.CQRS.Division.Handlers.Queries;
using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Divisions;

public class DivisionActualsHandlerTests
{
    private sealed record Fixture(
        Guid FiscalYearId,
        Guid ScorecardId,
        Guid PeriodId,
        Guid DivisionAId,
        Guid DivisionBId,
        Guid KpiId,
        Guid DivisionAccountUserId,
        Guid CorporateUserId);

    private static async Task<Fixture> CreateFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);
        var periods = calendar.GeneratePeriods(year);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "ECX Corporate Strategy 2026",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 100 };

        var divisionA = new Division { Id = Guid.NewGuid(), Name = "Information Technology", Code = "IT" };
        var divisionB = new Division { Id = Guid.NewGuid(), Name = "Trade Operations", Code = "TRO" };

        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = divisionA.Id,
            ScorecardId = scorecard.Id,
            PlanStatus = DivisionPlanStatus.Finalized
        };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = goal.Id,
            Name = "Digitize Trade Operations",
            Weight = 100
        };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            Name = "Systems Delivered",
            Unit = "count",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        var divisionAccount = new User
        {
            Id = Guid.NewGuid(),
            Username = "it",
            FullName = "IT Division Account",
            PasswordHash = "hash",
            DivisionId = divisionA.Id
        };
        var corporateUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "planner",
            FullName = "Corporate Planner",
            PasswordHash = "hash"
        };

        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Divisions.AddRange(divisionA, divisionB);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        lease.Context.Kpis.Add(kpi);
        lease.Context.Users.AddRange(divisionAccount, corporateUser);
        await lease.Context.SaveChangesAsync();

        return new Fixture(
            year.Id,
            scorecard.Id,
            periods[0].Id,
            divisionA.Id,
            divisionB.Id,
            kpi.Id,
            divisionAccount.Id,
            corporateUser.Id);
    }

    private static EnterDivisionActualValueCommandHandler CreateEnterHandler(
        TestDbContextLease lease, ICurrentUserService currentUserService, IAuditContext? auditContext = null)
        => new(
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            currentUserService,
            new TestSpmsJobScheduler(),
            auditContext ?? new AuditContext());

    private static GetDivisionActualValuesQueryHandler CreateGetHandler(
        TestDbContextLease lease, ICurrentUserService currentUserService)
        => new(
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            currentUserService);

    [Fact]
    public async Task EnterValue_DivisionAccountOwnDivision_Succeeds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var account = new TestCurrentUserService { UserId = fixture.DivisionAccountUserId, DivisionId = fixture.DivisionAId };
        var handler = CreateEnterHandler(lease, account);

        var response = await handler.Handle(new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.DivisionAId,
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 5
            }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(5, response.Data!.Value);
        Assert.Equal(1, await lease.Context.KpiActualValues.CountAsync());
    }

    [Fact]
    public async Task EnterValue_DivisionAccountOtherDivision_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var account = new TestCurrentUserService { UserId = Guid.NewGuid(), DivisionId = fixture.DivisionBId };
        var handler = CreateEnterHandler(lease, account);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new EnterDivisionActualValueCommandRequest
            {
                Dto = new EnterDivisionActualValueDto
                {
                    DivisionId = fixture.DivisionAId,
                    KpiId = fixture.KpiId,
                    FiscalPeriodId = fixture.PeriodId,
                    Value = 5
                }
            }, CancellationToken.None));

        Assert.Equal(0, await lease.Context.KpiActualValues.CountAsync());
    }

    [Fact]
    public async Task EnterValue_CorporateUser_AnyDivision_Succeeds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var handler = CreateEnterHandler(lease, new TestCurrentUserService { UserId = fixture.CorporateUserId });

        var response = await handler.Handle(new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.DivisionAId,
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 7
            }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(7, response.Data!.Value);
    }

    [Fact]
    public async Task EnterValue_WithOverrideReason_StampsReasonOnAuditEntry()
    {
        var auditContext = new AuditContext();
        await using var lease = await TestDbContextFactory.CreateAsync(null, auditContext);
        var fixture = await CreateFixtureAsync(lease);
        var handler = CreateEnterHandler(lease, new TestCurrentUserService { UserId = fixture.CorporateUserId }, auditContext);

        var response = await handler.Handle(new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.DivisionAId,
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 7,
                OverrideReason = "Approved year-end correction for the division plan."
            }
        }, CancellationToken.None);

        Assert.True(response.Success);

        var entry = await lease.Context.AuditLogEntries
            .SingleAsync(a => a.EntityType == nameof(KpiActualValue));
        Assert.Equal("Approved year-end correction for the division plan.", entry.Reason);
        Assert.NotEqual(default, entry.PerformedAt);
    }

    [Fact]
    public async Task GetValues_DivisionAccountOwnDivision_ReturnsValues()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var account = new TestCurrentUserService { UserId = fixture.DivisionAccountUserId, DivisionId = fixture.DivisionAId };

        await CreateEnterHandler(lease, account).Handle(new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.DivisionAId,
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 9
            }
        }, CancellationToken.None);

        var response = await CreateGetHandler(lease, account).Handle(new GetDivisionActualValuesQueryRequest
        {
            DivisionId = fixture.DivisionAId,
            FiscalYearId = fixture.FiscalYearId
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(9, response.Data!.Single().Value);
    }

    [Fact]
    public async Task GetValues_DivisionAccountOtherDivision_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var account = new TestCurrentUserService { UserId = Guid.NewGuid(), DivisionId = fixture.DivisionBId };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateGetHandler(lease, account).Handle(new GetDivisionActualValuesQueryRequest
            {
                DivisionId = fixture.DivisionAId,
                FiscalYearId = fixture.FiscalYearId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task EnterValue_KpiFromSiblingDivisionPlan_ThrowsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);

        // Division B has its own plan on the same corporate scorecard. A corporate user
        // must not be able to post division A's KPI under division B just by changing the
        // payload's DivisionId.
        var planB = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = fixture.DivisionBId,
            ScorecardId = fixture.ScorecardId,
            PlanStatus = DivisionPlanStatus.Finalized
        };
        var goalB = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = planB.Id,
            ParentGoalId = (await lease.Context.Goals.SingleAsync()).Id,
            Name = "Trade Ops Goal",
            Weight = 100
        };
        lease.Context.DivisionPlans.Add(planB);
        lease.Context.DivisionGoals.Add(goalB);
        await lease.Context.SaveChangesAsync();

        var handler = CreateEnterHandler(lease, new TestCurrentUserService { UserId = fixture.CorporateUserId });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new EnterDivisionActualValueCommandRequest
            {
                Dto = new EnterDivisionActualValueDto
                {
                    DivisionId = fixture.DivisionBId,
                    KpiId = fixture.KpiId,
                    FiscalPeriodId = fixture.PeriodId,
                    Value = 5
                }
            }, CancellationToken.None));

        Assert.Equal(0, await lease.Context.KpiActualValues.CountAsync());
    }

    [Fact]
    public async Task GetValues_DoesNotLeakSiblingDivisionKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);

        var planB = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = fixture.DivisionBId,
            ScorecardId = fixture.ScorecardId,
            PlanStatus = DivisionPlanStatus.Finalized
        };
        var goalB = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = planB.Id,
            ParentGoalId = (await lease.Context.Goals.SingleAsync()).Id,
            Name = "Trade Ops Goal",
            Weight = 100
        };
        var kpiB = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goalB.Id,
            Name = "Trade Ops KPI",
            Unit = "count",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        lease.Context.DivisionPlans.Add(planB);
        lease.Context.DivisionGoals.Add(goalB);
        lease.Context.Kpis.Add(kpiB);
        await lease.Context.SaveChangesAsync();

        var corporate = new TestCurrentUserService { UserId = fixture.CorporateUserId };
        await CreateEnterHandler(lease, corporate).Handle(new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.DivisionBId,
                KpiId = kpiB.Id,
                FiscalPeriodId = fixture.PeriodId,
                Value = 42
            }
        }, CancellationToken.None);

        var response = await CreateGetHandler(lease, corporate).Handle(new GetDivisionActualValuesQueryRequest
        {
            DivisionId = fixture.DivisionAId,
            FiscalYearId = fixture.FiscalYearId
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.DoesNotContain(response.Data!, v => v.KpiId == kpiB.Id);
    }
}
