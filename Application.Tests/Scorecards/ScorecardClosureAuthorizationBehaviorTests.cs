using Application.Behaviors;
using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Goals.Requests.Commands;
using Application.CQRS.Kpi.Requests.Commands;
using Application.CQRS.KpiTargets.Requests.Commands;
using Application.CQRS.Kra.Requests.Commands;
using Application.CQRS.PeriodLock.Requests.Commands;
using Application.CQRS.Scorecard.Requests.Commands;
using Application.DTOS.Division;
using Application.DTOS.Goal;
using Application.DTOS.Kpi;
using Application.DTOS.KpiTarget;
using Application.DTOS.Kra;
using Application.DTOS.PeriodLock;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Repositories;
using MediatR;

namespace Application.Tests.Scorecards;

public class ScorecardClosureAuthorizationBehaviorTests
{
    [Fact]
    public async Task UpdateScorecard_RejectsClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<UpdateScorecardCommandRequest, Response<ScorecardDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new UpdateScorecardCommandRequest { Id = f.ScorecardId, Name = "Renamed" },
                _ => Task.FromResult(new Response<ScorecardDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteScorecard_RejectsClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<DeleteScorecardCommandRequest, Unit>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new DeleteScorecardCommandRequest { Id = f.ScorecardId },
                _ => Task.FromResult(Unit.Value), CancellationToken.None));
    }

    [Fact]
    public async Task CreateScorecard_RejectsClosedFiscalYear()
    {
        await using var lease = await NewLeaseAsync(closeFiscalYear: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<CreateScorecardCommandRequest, Response<ScorecardDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new CreateScorecardCommandRequest { FiscalYearId = f.FiscalYearId, Name = "New" },
                _ => Task.FromResult(new Response<ScorecardDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task CreateScorecard_AllowsOpenFiscalYear()
    {
        await using var lease = await NewLeaseAsync();
        var f = lease.Fixture;

        var behavior = CreateBehavior<CreateScorecardCommandRequest, Response<ScorecardDto>>(lease);
        var invoked = false;
        await behavior.Handle(new CreateScorecardCommandRequest { FiscalYearId = f.FiscalYearId, Name = "New" },
            _ => { invoked = true; return Task.FromResult(new Response<ScorecardDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task CloneScorecard_RejectsClosedTargetFiscalYear()
    {
        await using var lease = await NewLeaseAsync(closeFiscalYear: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<CloneScorecardCommandRequest, Response<CloneScorecardResultDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new CloneScorecardCommandRequest
            {
                SourceScorecardId = f.ScorecardId,
                TargetFiscalYearId = f.FiscalYearId,
            }, _ => Task.FromResult(new Response<CloneScorecardResultDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task GoalWrites_RejectClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var create = CreateBehavior<CreateGoalCommandRequest, Response<CreateGoalDto>>(lease);
        var update = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease);
        var delete = CreateBehavior<DeleteGoalCommandRequest, Unit>(lease);

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            create.Handle(new CreateGoalCommandRequest
            {
                Dto = new CreateGoalDto { ScorecardId = f.ScorecardId, Name = "G", Weight = 100 }
            }, _ => Task.FromResult(new Response<CreateGoalDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            update.Handle(new UpdateGoalCommandRequest { Id = f.GoalId, Name = "G2" },
                _ => Task.FromResult(new Response<GoalDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            delete.Handle(new DeleteGoalCommandRequest { Id = f.GoalId },
                _ => Task.FromResult(Unit.Value), CancellationToken.None));
    }

    [Fact]
    public async Task KraWrites_RejectClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var create = CreateBehavior<CreateKraCommandRequest, Response<KraDto>>(lease);
        var update = CreateBehavior<UpdateKraCommandRequest, Response<KraDto>>(lease);
        var delete = CreateBehavior<DeleteKraCommandRequest, Unit>(lease);

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            create.Handle(new CreateKraCommandRequest { ScorecardId = f.ScorecardId, GoalId = f.GoalId, Name = "K", Weight = 100 },
                _ => Task.FromResult(new Response<KraDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            update.Handle(new UpdateKraCommandRequest { Id = f.KraId, Name = "K2" },
                _ => Task.FromResult(new Response<KraDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            delete.Handle(new DeleteKraCommandRequest { Id = f.KraId },
                _ => Task.FromResult(Unit.Value), CancellationToken.None));
    }

    [Fact]
    public async Task KpiWrites_RejectClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var create = CreateBehavior<CreateKpiCommandRequest, Response<KpiDto>>(lease);
        var update = CreateBehavior<UpdateKpiCommandRequest, Response<KpiDto>>(lease);
        var delete = CreateBehavior<DeleteKpiCommandRequest, Unit>(lease);

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            create.Handle(new CreateKpiCommandRequest { ScorecardId = f.ScorecardId, Name = "KP", Weight = 100 },
                _ => Task.FromResult(new Response<KpiDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            update.Handle(new UpdateKpiCommandRequest { Id = f.KpiId, Name = "KP2" },
                _ => Task.FromResult(new Response<KpiDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            delete.Handle(new DeleteKpiCommandRequest { Id = f.KpiId },
                _ => Task.FromResult(Unit.Value), CancellationToken.None));
    }

    [Fact]
    public async Task SetKpiTargets_RejectsClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<SetKpiTargetsCommandRequest, Response<GetKpiTargetsDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new SetKpiTargetsCommandRequest
            {
                Dto = new SetKpiTargetsDto { KpiId = f.KpiId, FiscalYearId = f.FiscalYearId, MonthlyTargets = new() { 10 } }
            }, _ => Task.FromResult(new Response<GetKpiTargetsDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task SetKpiTargetsBulk_RejectsWhenAnyKpiIsClosed()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<SetKpiTargetsBulkCommandRequest, Response<SetKpiTargetsBulkResultDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new SetKpiTargetsBulkCommandRequest
            {
                Dto = new SetKpiTargetsBulkDto
                {
                    FiscalYearId = f.FiscalYearId,
                    Targets = new List<SetKpiTargetsItemDto>
                    {
                        new() { KpiId = f.KpiId, MonthlyTargets = new() { 20 } }
                    }
                }
            }, _ => Task.FromResult(new Response<SetKpiTargetsBulkResultDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task SetPeriodLockConfig_RejectsClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<SetPeriodLockConfigCommandRequest, Response<PeriodLockStatusItemDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new SetPeriodLockConfigCommandRequest
            {
                Dto = new SetPeriodLockConfigDto { ScorecardId = f.ScorecardId, FiscalPeriodId = Guid.NewGuid(), Lock = true }
            }, _ => Task.FromResult(new Response<PeriodLockStatusItemDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task DivisionPlanWrites_RejectClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var createGoal = CreateBehavior<CreateDivisionGoalCommandRequest, Response<DivisionGoalDto>>(lease);
        var updateGoal = CreateBehavior<UpdateDivisionGoalCommandRequest, Response<DivisionGoalDto>>(lease);
        var deleteGoal = CreateBehavior<DeleteDivisionGoalCommandRequest, bool>(lease);
        var createKpi = CreateBehavior<CreateDivisionKpiCommandRequest, Response<DivisionKpiDto>>(lease);
        var updateKpi = CreateBehavior<UpdateDivisionKpiCommandRequest, Response<DivisionKpiDto>>(lease);
        var deleteKpi = CreateBehavior<DeleteDivisionKpiCommandRequest, bool>(lease);

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            createGoal.Handle(new CreateDivisionGoalCommandRequest { DivisionId = f.DivisionId, ScorecardId = f.ScorecardId, ParentGoalId = f.GoalId, Name = "DG", Weight = 100 },
                _ => Task.FromResult(new Response<DivisionGoalDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            updateGoal.Handle(new UpdateDivisionGoalCommandRequest { DivisionGoalId = f.DivisionGoalId, Name = "DG2" },
                _ => Task.FromResult(new Response<DivisionGoalDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            deleteGoal.Handle(new DeleteDivisionGoalCommandRequest { DivisionGoalId = f.DivisionGoalId },
                _ => Task.FromResult(false), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            createKpi.Handle(new CreateDivisionKpiCommandRequest { DivisionId = f.DivisionId, ScorecardId = f.ScorecardId, DivisionGoalId = f.DivisionGoalId, Name = "DK", Weight = 100 },
                _ => Task.FromResult(new Response<DivisionKpiDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            updateKpi.Handle(new UpdateDivisionKpiCommandRequest { DivisionKpiId = f.KpiId, Name = "DK2" },
                _ => Task.FromResult(new Response<DivisionKpiDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            deleteKpi.Handle(new DeleteDivisionKpiCommandRequest { DivisionKpiId = f.KpiId },
                _ => Task.FromResult(false), CancellationToken.None));
    }

    [Fact]
    public async Task DivisionContributionAndFinalize_RejectClosedScorecard()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var contributions = CreateBehavior<SetDivisionContributionsCommandRequest, Response<DivisionPlanDto>>(lease);
        var finalize = CreateBehavior<FinalizeDivisionPlanCommandRequest, Response<DivisionPlanSummaryDto>>(lease);

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            contributions.Handle(new SetDivisionContributionsCommandRequest { DivisionId = f.DivisionId, ScorecardId = f.ScorecardId, GoalIds = new() { f.GoalId } },
                _ => Task.FromResult(new Response<DivisionPlanDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            finalize.Handle(new FinalizeDivisionPlanCommandRequest { DivisionId = f.DivisionId, ScorecardId = f.ScorecardId },
                _ => Task.FromResult(new Response<DivisionPlanSummaryDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task ScorecardClosedViaClosedFiscalYear_IsRejected()
    {
        // Scorecard itself is still Active but its fiscal year has been closed by the job.
        await using var lease = await NewLeaseAsync(closeFiscalYear: true);
        var f = lease.Fixture;

        var behavior = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new UpdateGoalCommandRequest { Id = f.GoalId, Name = "G2" },
                _ => Task.FromResult(new Response<GoalDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task OpenScorecard_AllowsWrites()
    {
        await using var lease = await NewLeaseAsync();
        var f = lease.Fixture;

        var updateGoal = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease);
        var setTargets = CreateBehavior<SetKpiTargetsCommandRequest, Response<GetKpiTargetsDto>>(lease);
        var updateDivisionGoal = CreateBehavior<UpdateDivisionGoalCommandRequest, Response<DivisionGoalDto>>(lease);
        var configureLock = CreateBehavior<SetPeriodLockConfigCommandRequest, Response<PeriodLockStatusItemDto>>(lease);

        var invokedGoal = false;
        await updateGoal.Handle(new UpdateGoalCommandRequest { Id = f.GoalId, Name = "G2" },
            _ => { invokedGoal = true; return Task.FromResult(new Response<GoalDto>()); }, CancellationToken.None);

        var invokedTargets = false;
        await setTargets.Handle(new SetKpiTargetsCommandRequest
        {
            Dto = new SetKpiTargetsDto { KpiId = f.KpiId, FiscalYearId = f.FiscalYearId, MonthlyTargets = new() { 10 } }
        }, _ => { invokedTargets = true; return Task.FromResult(new Response<GetKpiTargetsDto>()); }, CancellationToken.None);

        var invokedDivision = false;
        await updateDivisionGoal.Handle(new UpdateDivisionGoalCommandRequest { DivisionGoalId = f.DivisionGoalId, Name = "DG2" },
            _ => { invokedDivision = true; return Task.FromResult(new Response<DivisionGoalDto>()); }, CancellationToken.None);

        var invokedLock = false;
        await configureLock.Handle(new SetPeriodLockConfigCommandRequest
        {
            Dto = new SetPeriodLockConfigDto { ScorecardId = f.ScorecardId, FiscalPeriodId = Guid.NewGuid(), Lock = true }
        }, _ => { invokedLock = true; return Task.FromResult(new Response<PeriodLockStatusItemDto>()); }, CancellationToken.None);

        Assert.True(invokedGoal);
        Assert.True(invokedTargets);
        Assert.True(invokedDivision);
        Assert.True(invokedLock);
    }

    [Fact]
    public async Task CreateDivision_IsNotScorecardOwned_AndIsAllowed()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);

        var behavior = CreateBehavior<CreateDivisionCommandRequest, Response<DivisionDto>>(lease);
        var invoked = false;
        await behavior.Handle(new CreateDivisionCommandRequest { Name = "Div", Code = "D1" },
            _ => { invoked = true; return Task.FromResult(new Response<DivisionDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task UnknownEntity_DoesNotThrowClosure_BeforeHandlerNotFound()
    {
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        // An id that does not exist: closure guard ignores it so the handler's own
        // NotFoundException is what surfaces.
        var behavior = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease);
        var invoked = false;
        await behavior.Handle(new UpdateGoalCommandRequest { Id = Guid.NewGuid(), Name = "G2" },
            _ => { invoked = true; return Task.FromResult(new Response<GoalDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task FiscalYear_DuringFinalMonthGrace_IsStillOpen()
    {
        // Confirmed business rule (final-month grace): structural edits for a year ending
        // 2100-06-30 remain permitted through the grace deadline 2100-07-07, inclusive;
        // on 2100-07-05 the closure guard must not reject the write.
        await using var lease = await NewLeaseAsync();
        var f = lease.Fixture;

        var clock = TestBusinessClock.On(new DateTime(2100, 7, 5, 8, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease, clock);
        var invoked = false;
        await behavior.Handle(new UpdateGoalCommandRequest { Id = f.GoalId, Name = "G2" },
            _ => { invoked = true; return Task.FromResult(new Response<GoalDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task FiscalYear_AfterGraceExpiry_IsRejected()
    {
        // FR-703 (item: closure timing): once the final-month grace period has expired
        // (from 2100-07-08, one day after the grace deadline 2100-07-07) the year is
        // read-only even when the nightly closure job has not flipped its status yet.
        // The scorecard itself is still Active.
        await using var lease = await NewLeaseAsync();
        var f = lease.Fixture;

        var clock = TestBusinessClock.On(new DateTime(2100, 7, 8, 8, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease, clock);
        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(new UpdateGoalCommandRequest { Id = f.GoalId, Name = "G2" },
                _ => Task.FromResult(new Response<GoalDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task FiscalYear_OnItsEndDateInBusinessTime_IsStillOpen()
    {
        // The year closes at 23:59 EAT on its end date; on the end date itself writes
        // are still allowed (the closure job runs the following morning).
        await using var lease = await NewLeaseAsync();
        var f = lease.Fixture;

        var clock = TestBusinessClock.On(new DateTime(2100, 6, 30, 20, 30, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior<UpdateGoalCommandRequest, Response<GoalDto>>(lease, clock);
        var invoked = false;
        await behavior.Handle(new UpdateGoalCommandRequest { Id = f.GoalId, Name = "G2" },
            _ => { invoked = true; return Task.FromResult(new Response<GoalDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task TargetsAndStructure_OnClosedScorecard_HaveNoOverrideBypass()
    {
        // ECX-approved business rule (FR-703): the `actuals:override` permission only ever
        // unlocks actual-value correction (honored by LockAuthorizationBehavior). It must
        // NOT reopen targets or scorecard structure. The closure authorization path has no
        // user dependency at all, so target/KRA/KPI/structure writes can never succeed on a
        // closed scorecard no matter what permissions the actor holds.
        await using var lease = await NewLeaseAsync(closedScorecard: true);
        var f = lease.Fixture;

        var setTargets = CreateBehavior<SetKpiTargetsCommandRequest, Response<GetKpiTargetsDto>>(lease);
        var updateScorecard = CreateBehavior<UpdateScorecardCommandRequest, Response<ScorecardDto>>(lease);
        var createKpi = CreateBehavior<CreateKpiCommandRequest, Response<KpiDto>>(lease);

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            setTargets.Handle(new SetKpiTargetsCommandRequest
            {
                Dto = new SetKpiTargetsDto { KpiId = f.KpiId, FiscalYearId = f.FiscalYearId, MonthlyTargets = new() { 10 } }
            }, _ => Task.FromResult(new Response<GetKpiTargetsDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            updateScorecard.Handle(new UpdateScorecardCommandRequest { Id = f.ScorecardId, Name = "Renamed" },
                _ => Task.FromResult(new Response<ScorecardDto>()), CancellationToken.None));

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            createKpi.Handle(new CreateKpiCommandRequest { ScorecardId = f.ScorecardId, Name = "KP", Weight = 100 },
                _ => Task.FromResult(new Response<KpiDto>()), CancellationToken.None));
    }

    [Fact]
    public void ClosureAuthorization_HasNoDependencyOnOverridePermission()
    {
        // Structural proof of the override guarantee: no closure path can consult the
        // user's permissions, so `actuals:override` cannot widen it. Override permission is
        // only honored by LockAuthorizationBehavior for actual-value commands.
        var constructor = typeof(ScorecardClosureAuthorizationBehavior<,>).GetConstructors().Single();
        Assert.DoesNotContain(
            constructor.GetParameters(),
            p => p.ParameterType.Name == nameof(ICurrentUserService));
    }

    private static ScorecardClosureAuthorizationBehavior<TRequest, TResponse> CreateBehavior<TRequest, TResponse>(
        SeedLease lease,
        TestBusinessClock? businessClock = null)
        where TRequest : IBaseRequest
    {
        var guard = new ScorecardClosureGuard(
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            businessClock ?? TestBusinessClock.Real());

        return new ScorecardClosureAuthorizationBehavior<TRequest, TResponse>(
            guard,
            new GoalRepository(lease.Context),
            new KraRepository(lease.Context),
            new KpiRepository(lease.Context),
            new DivisionGoalRepository(lease.Context));
    }

    private static async Task<SeedLease> NewLeaseAsync(bool closedScorecard = false, bool closeFiscalYear = false)
    {
        var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await SeedAsync(lease, closedScorecard, closeFiscalYear);
        return new SeedLease(lease, fixture);
    }

    private static async Task<ClosureFixture> SeedAsync(TestDbContextLease lease, bool closedScorecard, bool closeFiscalYear)
    {
        // 2099 keeps the seeded year safely in the future no matter when the suite runs,
        // so the default (real) business clock treats it as open unless a test closes it.
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2099);
        var periods = calendar.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Closure Guard",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var division = new Division { Id = Guid.NewGuid(), Name = "Division A", Code = "DIV-A" };
        var plan = new DivisionPlan { Id = Guid.NewGuid(), DivisionId = division.Id, ScorecardId = scorecard.Id };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = goal.Id,
            Name = "Division Goal",
            Weight = 100
        };

        if (closeFiscalYear)
        {
            fiscalYear.FiscalYearStatus = FiscalYearStatus.Closed;
        }
        if (closedScorecard)
        {
            scorecard.ScorecardStatus = ScorecardStatus.Closed;
        }

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.Divisions.Add(division);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        await lease.Context.SaveChangesAsync();

        return new ClosureFixture(
            fiscalYear.Id,
            scorecard.Id,
            goal.Id,
            kra.Id,
            kpi.Id,
            division.Id,
            plan.Id,
            divisionGoal.Id);
    }

    private sealed record ClosureFixture(
        Guid FiscalYearId,
        Guid ScorecardId,
        Guid GoalId,
        Guid KraId,
        Guid KpiId,
        Guid DivisionId,
        Guid DivisionPlanId,
        Guid DivisionGoalId);

    private sealed record SeedLease(TestDbContextLease Lease, ClosureFixture Fixture) : IAsyncDisposable
    {
        public SpmsDbContext Context => Lease.Context;

        public ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
}