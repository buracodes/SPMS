using Application.Behaviors;
using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.DTOS.Division;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.FiscalYears;

public class LockAuthorizationBehaviorTests
{
    [Fact]
    public async Task Allows_WhenPeriodOpen()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureWithUnlockAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            UserId = fixture.OwnerId
        });

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 100 }
        };

        var invoked = false;
        await behavior.Handle(command, _ => { invoked = true; return Task.FromResult(new Response<ActualValueDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Allows_WhenPeriodOpen_ForDivisionOwner()
    {
        // FR-906: division KPIs are owned by a division, so a division account must be
        // able to enter actuals during the open window even though Kpi.OwnerId is null.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateDivisionFixtureAsync(lease);
        // July 2025 (fiscal month 1) is open during 1-7 August 2025.
        var clock = TestBusinessClock.On(new DateTime(2025, 8, 3, 9, 0, 0, DateTimeKind.Utc));
        var behavior = CreateDivisionBehavior(lease, new TestCurrentUserService
        {
            DivisionId = fixture.OwnerDivisionId
        }, clock);

        var command = new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.OwnerDivisionId,
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 10
            }
        };

        var invoked = false;
        await behavior.Handle(command, _ => { invoked = true; return Task.FromResult(new Response<DivisionActualValueDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Blocks_WhenPeriodOpen_ForAnotherDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateDivisionFixtureAsync(lease);
        var clock = TestBusinessClock.On(new DateTime(2025, 8, 3, 9, 0, 0, DateTimeKind.Utc));
        var behavior = CreateDivisionBehavior(lease, new TestCurrentUserService
        {
            DivisionId = fixture.OtherDivisionId
        }, clock);

        var command = new EnterDivisionActualValueCommandRequest
        {
            Dto = new EnterDivisionActualValueDto
            {
                DivisionId = fixture.OtherDivisionId,
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 10
            }
        };

        await Assert.ThrowsAsync<PeriodLockedException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<DivisionActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task Blocks_WhenPeriodOpen_ForDivisionOwner_OnCorporateActualCommand()
    {
        // Division ownership only applies to the division actuals path; corporate KPI
        // actuals stay limited to the individual KPI owner or an override user.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateDivisionFixtureAsync(lease);
        var clock = TestBusinessClock.On(new DateTime(2025, 8, 3, 9, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            DivisionId = fixture.OwnerDivisionId
        }, clock);

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 10
            }
        };

        await Assert.ThrowsAsync<PeriodLockedException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task Blocks_WhenPeriodLocked_WithoutOverride()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureWithLockAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService());

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 100 }
        };

        await Assert.ThrowsAsync<PeriodLockedException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task Allows_WhenLocked_WithOverridePermissionAndReason()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureWithLockAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            Permissions = new[] { Permissions.ActualsOverride }
        });

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 100,
                OverrideReason = "Admin correction after approved exception."
            }
        };

        var invoked = false;
        await behavior.Handle(command, _ => { invoked = true; return Task.FromResult(new Response<ActualValueDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task RequiresOverrideReason_WhenLocked_AndUserCanOverride()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureWithLockAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            Permissions = new[] { Permissions.ActualsOverride }
        });

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 100 }
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task Blocks_WhenScorecardClosed_WithoutOverride()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateClosedScorecardFixtureAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService());

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 100 }
        };

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task RequiresOverrideReason_WhenScorecardClosed_AndUserCanOverride()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateClosedScorecardFixtureAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            Permissions = new[] { Permissions.ActualsOverride }
        });

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 100 }
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task Allows_WhenScorecardClosed_WithOverridePermissionAndReason()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateClosedScorecardFixtureAsync(lease);
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            Permissions = new[] { Permissions.ActualsOverride }
        });

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 100,
                OverrideReason = "Audited year-end correction."
            }
        };

        var invoked = false;
        await behavior.Handle(command, _ => { invoked = true; return Task.FromResult(new Response<ActualValueDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Allows_FinalMonthEntry_DuringGracePeriod_ForKpiOwner()
    {
        // Confirmed business rule (final-month grace): the fiscal year 2025/26 ends
        // 2026-06-30, so final-month (June) actual entry stays open for the KPI owner
        // through the grace deadline 2026-07-07, inclusive — even though the calendar
        // year has already rolled over. No override permission or reason needed.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateBaseFixtureAsync(lease);
        var clock = TestBusinessClock.On(new DateTime(2026, 7, 7, 8, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            UserId = fixture.OwnerId
        }, clock);

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.FinalPeriodId,
                Value = 100
            }
        };

        var invoked = false;
        await behavior.Handle(command, _ => { invoked = true; return Task.FromResult(new Response<ActualValueDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Blocks_FinalMonthEntry_OnceGracePeriodExpires_KpiOwnerWithoutOverride()
    {
        // Normal entry is blocked from 2026-07-08 (the first day after the grace
        // deadline): the year is read-only for the KPI owner and requires an audited
        // Admin/Power User override to change.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateBaseFixtureAsync(lease);
        var clock = TestBusinessClock.On(new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            UserId = fixture.OwnerId
        }, clock);

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.FinalPeriodId,
                Value = 100
            }
        };

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task Blocks_WhenFiscalYearGraceExpired_EvenForKpiOwner()
    {
        // FR-703 (item: closure timing): the fiscal year's final-month grace has expired
        // in ECX business time but its status is still Open (the nightly closure job has
        // not run yet). The scorecard itself is Active, the caller is the KPI owner, and
        // the write must still be blocked — the deadline never depends on the job.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateBaseFixtureAsync(lease);
        var clock = TestBusinessClock.On(new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            UserId = fixture.OwnerId
        }, clock);

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto { KpiId = fixture.KpiId, FiscalPeriodId = fixture.PeriodId, Value = 100 }
        };

        await Assert.ThrowsAsync<ScorecardClosedException>(() =>
            behavior.Handle(command, _ => Task.FromResult(new Response<ActualValueDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task AllowsWhenGraceExpired_WithOverridePermissionAndReason()
    {
        // Audited actual-value correction on a grace-expired-but-not-yet-closed year is
        // the only exception to the read-only rule, and it requires the override
        // permission and an explicit reason (which the handler persists into the audit
        // trail).
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateBaseFixtureAsync(lease);
        var clock = TestBusinessClock.On(new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc));
        var behavior = CreateBehavior(lease, new TestCurrentUserService
        {
            Permissions = new[] { Permissions.ActualsOverride }
        }, clock);

        var command = new EnterActualValueCommandRequest
        {
            Dto = new EnterActualValueDto
            {
                KpiId = fixture.KpiId,
                FiscalPeriodId = fixture.PeriodId,
                Value = 100,
                OverrideReason = "Audited year-end correction."
            }
        };

        var invoked = false;
        await behavior.Handle(command, _ => { invoked = true; return Task.FromResult(new Response<ActualValueDto>()); }, CancellationToken.None);

        Assert.True(invoked);
    }

    private static async Task<(Guid KpiId, Guid ScorecardId, Guid PeriodId)> CreateClosedScorecardFixtureAsync(TestDbContextLease lease)
    {
        var fixture = await CreateBaseFixtureAsync(lease);
        var scorecard = await lease.Context.Scorecards.FirstAsync(s => s.Id == fixture.ScorecardId);
        scorecard.ScorecardStatus = ScorecardStatus.Closed;
        await lease.Context.SaveChangesAsync();
        return (fixture.KpiId, fixture.ScorecardId, fixture.PeriodId);
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

    private static LockAuthorizationBehavior<EnterActualValueCommandRequest, Response<ActualValueDto>> CreateBehavior(
        TestDbContextLease lease,
        ICurrentUserService currentUserService,
        TestBusinessClock? businessClock = null)
    {
        // Default clock pins the business date to the fixture's last fiscal-year day
        // (2026-06-30), so the seeded 2025 fiscal year stays open unless a test moves the
        // clock past the end date.
        return new LockAuthorizationBehavior<EnterActualValueCommandRequest, Response<ActualValueDto>>(
            new KpiRepository(lease.Context),
            CreateLockService(lease),
            currentUserService,
            businessClock ?? TestBusinessClock.On(new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc)));
    }

    private static LockAuthorizationBehavior<EnterDivisionActualValueCommandRequest, Response<DivisionActualValueDto>> CreateDivisionBehavior(
        TestDbContextLease lease,
        ICurrentUserService currentUserService,
        TestBusinessClock? businessClock = null)
    {
        return new LockAuthorizationBehavior<EnterDivisionActualValueCommandRequest, Response<DivisionActualValueDto>>(
            new KpiRepository(lease.Context),
            CreateLockService(lease),
            currentUserService,
            businessClock ?? TestBusinessClock.On(new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc)));
    }

    private static async Task<(Guid KpiId, Guid ScorecardId, Guid PeriodId)> CreateFixtureWithLockAsync(TestDbContextLease lease)
    {
        var fixture = await CreateBaseFixtureAsync(lease);
        lease.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = fixture.ScorecardId,
            FiscalPeriodId = fixture.PeriodId,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2,
            LockedAtUtc = new DateTime(2025, 7, 20, 8, 0, 0, DateTimeKind.Utc)
        });
        await lease.Context.SaveChangesAsync();
        return (fixture.KpiId, fixture.ScorecardId, fixture.PeriodId);
    }

    private static async Task<(Guid KpiId, Guid OwnerId, Guid ScorecardId, Guid PeriodId)> CreateFixtureWithUnlockAsync(TestDbContextLease lease)
    {
        var fixture = await CreateBaseFixtureAsync(lease);
        lease.Context.PeriodLockConfigs.Add(new PeriodLockConfig
        {
            Id = Guid.NewGuid(),
            ScorecardId = fixture.ScorecardId,
            FiscalPeriodId = fixture.PeriodId,
            EntryOpenDay = 1,
            CutoffDay = 7,
            ReminderDaysBeforeCutoff = 2,
            LockedAtUtc = new DateTime(2025, 7, 20, 8, 0, 0, DateTimeKind.Utc),
            UnlockedAtUtc = new DateTime(2025, 7, 21, 8, 0, 0, DateTimeKind.Utc)
        });
        await lease.Context.SaveChangesAsync();
        return (fixture.KpiId, fixture.OwnerId, fixture.ScorecardId, fixture.PeriodId);
    }

    private static async Task<(Guid KpiId, Guid PeriodId, Guid OwnerDivisionId, Guid OtherDivisionId)> CreateDivisionFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var periods = calendar.GeneratePeriods(fiscalYear);
        var july = periods.First(p => p.FiscalMonth == 1);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Division Lock Behavior",
            Scope = ScorecardScope.Corporate
        };
        var parentGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Parent Goal", Weight = 100 };
        var ownerDivision = new Domain.Entities.Division { Id = Guid.NewGuid(), Name = "Information Technology", Code = "IT" };
        var otherDivision = new Domain.Entities.Division { Id = Guid.NewGuid(), Name = "Trade Operations", Code = "TRO" };
        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = ownerDivision.Id,
            ScorecardId = scorecard.Id,
            PlanStatus = DivisionPlanStatus.Finalized
        };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = parentGoal.Id,
            Name = "Division Goal",
            Weight = 100
        };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            DivisionOwnerId = ownerDivision.Id,
            Name = "Systems Delivered",
            Weight = 100,
            IsActive = true
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(parentGoal);
        lease.Context.Divisions.AddRange(ownerDivision, otherDivision);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        lease.Context.Kpis.Add(kpi);
        await lease.Context.SaveChangesAsync();

        return (kpi.Id, july.Id, ownerDivision.Id, otherDivision.Id);
    }

    private static async Task<(Guid KpiId, Guid OwnerId, Guid ScorecardId, Guid PeriodId, Guid FinalPeriodId)> CreateBaseFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var periods = calendar.GeneratePeriods(fiscalYear);
        var july = periods.First(p => p.FiscalMonth == 1);
        var june = periods.First(p => p.FiscalMonth == 12);
        var ownerId = Guid.NewGuid();

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Lock Behavior",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };
        var owner = new User
        {
            Id = ownerId,
            Username = "owner",
            FullName = "Kpi Owner",
        };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            OwnerId = ownerId,
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
        lease.Context.Users.Add(owner);
        lease.Context.Kpis.Add(kpi);
        lease.Context.FiscalPeriods.AddRange(periods);
        await lease.Context.SaveChangesAsync();

        return (kpi.Id, ownerId, scorecard.Id, july.Id, june.Id);
    }
}