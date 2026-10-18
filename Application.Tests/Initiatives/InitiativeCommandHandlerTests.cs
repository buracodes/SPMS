using Application.CQRS.Initiative;
using Application.CQRS.Initiative.Handlers.Commands;
using Application.CQRS.Initiative.Requests.Commands;
using Application.Dashboard;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Initiatives;

/// <summary>
/// FR-403 progress reporting, FR-405 closure and the dependency graph rules, exercised through the
/// real handlers against a real database so the history rows and calculated figures are covered
/// end to end.
/// </summary>
public class InitiativeCommandHandlerTests
{
    private static InitiativeGuard BuildGuard(TestDbContextLease lease, TestCurrentUserService user) => new(
        new GoalRepository(lease.Context),
        new KraRepository(lease.Context),
        new KpiRepository(lease.Context),
        new UserRepository(lease.Context),
        user);

    private static InitiativeDtoFactory BuildFactory(TestRagThresholdService? thresholds = null) => new(
        new InitiativeProgressCalculator(),
        new InitiativeBudgetCalculator(),
        thresholds ?? new TestRagThresholdService());

    private static async Task<User> SeedUserAsync(TestDbContextLease lease, string fullName)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"user-{suffix[..10]}",
            FullName = fullName,
            PasswordHash = "HASH",
            Email = $"{suffix}@spms.local",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        lease.Context.Users.Add(user);
        await lease.Context.SaveChangesAsync();
        return user;
    }

    private static async Task<Initiative> SeedInitiativeAsync(
        TestDbContextLease lease,
        User owner,
        InitiativeStatus status = InitiativeStatus.InProgress,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var initiative = new Initiative
        {
            Id = Guid.NewGuid(),
            Name = $"Initiative {Guid.NewGuid():N}"[..20],
            OwnerId = owner.Id,
            Priority = InitiativePriority.High,
            Status = status,
            Budget = 1_000m,
            StartDate = startDate ?? new DateTime(2026, 1, 1),
            EndDate = endDate ?? new DateTime(2026, 12, 31),
            CreatedDate = DateTime.UtcNow
        };

        lease.Context.Initiatives.Add(initiative);
        await lease.Context.SaveChangesAsync();
        return initiative;
    }

    private static async Task<Milestone> SeedMilestoneAsync(
        TestDbContextLease lease,
        Initiative initiative,
        decimal weight,
        int completion,
        string name = "Phase")
    {
        var milestone = new Milestone
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            Name = name,
            Weight = weight,
            CompletionPercentage = completion,
            DueDate = new DateTime(2026, 6, 30),
            CreatedDate = DateTime.UtcNow
        };

        lease.Context.Milestones.Add(milestone);
        await lease.Context.SaveChangesAsync();
        return milestone;
    }

    private static TestCurrentUserService Owner(User owner, params string[] permissions) => new()
    {
        UserId = owner.Id,
        UserName = "owner",
        Roles = new[] { "DataInputUser" },
        Permissions = permissions
    };

    // ------------------------------------------------------------------ FR-403 progress

    [Fact]
    public async Task UpdateMilestoneProgress_RecalculatesProgressAndWritesHistory()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var milestone = await SeedMilestoneAsync(lease, initiative, 60m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesUpdateProgress);
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        var response = await handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 75,
                    Comments = "Vendor delivered phase 1."
                }
            },
            CancellationToken.None);

        Assert.True(response.Success);
        // 60 weight * 75% / 100 = 45% overall.
        Assert.Equal(45m, response.Data.ProgressPercentage);
        Assert.Equal(75, response.Data.Milestones.Single().CompletionPercentage);

var log = Assert.Single(await lease.Context.InitiativeUpdateLogs.AsNoTracking().ToListAsync());
        Assert.Equal("ProgressUpdated", log.ChangeType);
        Assert.Equal(milestone.Id, log.MilestoneId);
        Assert.Equal(initiative.Id, log.InitiativeId);
        Assert.Contains("75", log.Details);
        // The audit interceptor stamps the acting user, satisfying the FR-403 history requirement.
        Assert.False(string.IsNullOrWhiteSpace(log.CreatedBy.ToString()));
    }

    [Fact]
    public async Task UpdateMilestoneProgress_RejectsCompletionOutsideBounds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesUpdateProgress);
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 140
                }
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMilestoneProgress_NonOwnerWithoutPermission_IsForbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var other = await SeedUserAsync(lease, "Someone Else");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = new TestCurrentUserService
        {
            UserId = other.Id,
            UserName = "other",
            Roles = new[] { "DataInputUser" },
            Permissions = Array.Empty<string>()
        };

        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 50
                }
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMilestoneProgress_ReachesOneHundred_CompletesInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 50);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesUpdateProgress);
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        var response = await handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 100
                }
            },
            CancellationToken.None);

Assert.Equal(100m, response.Data.ProgressPercentage);

        // FRD lifecycle: 100% progress makes the initiative ready, it does not complete it.
        Assert.NotEqual(InitiativeStatus.Completed, response.Data.Status);
        Assert.True(response.Data.IsReadyForCompletion);
        Assert.Null(response.Data.NotReadyReason);

        // The progress response carries the FRD band the detail page paints the progress bar with.
        Assert.Equal(DashboardRag.Green, response.Data.ProgressRagStatus);

        // Nothing about the recorded status may be persisted by a progress edit.
        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.NotEqual(InitiativeStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task UpdateMilestoneProgress_FirstReportedProgress_StartsANotStartedInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.NotStarted);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesUpdateProgress);
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        await handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 40
                }
            },
            CancellationToken.None);

        // FRD lifecycle: the first recorded progress starts the initiative.
        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.InProgress, stored.Status);

        // The status transition is audited alongside the progress entry, never instead of it.
        var logs = await lease.Context.InitiativeUpdateLogs.AsNoTracking().ToListAsync();
        Assert.Single(logs, l => l.ChangeType == "ProgressUpdated");
        var transition = Assert.Single(logs, l => l.ChangeType == "StatusChanged");
        Assert.Equal("NotStarted", transition.PreviousValue);
        Assert.Equal("InProgress", transition.NewValue);
        Assert.Contains("Status changed from NotStarted to InProgress", transition.Details);
    }

    [Fact]
    public async Task UpdateMilestoneProgress_OwnerWithoutProgressPermission_StartsTheInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.NotStarted);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        // The assigned owner needs no initiative permission: ownership itself authorises progress.
        var user = new TestCurrentUserService
        {
            UserId = owner.Id,
            UserName = "owner",
            Roles = new[] { "DataInputUser" },
            Permissions = Array.Empty<string>()
        };
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        var response = await handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 100
                }
            },
            CancellationToken.None);

        // 100% starts the initiative but does not complete it — closure still owns Completed.
        Assert.Equal(InitiativeStatus.InProgress, response.Data.Status);
        Assert.True(response.Data.IsReadyForCompletion);
        Assert.NotEqual(InitiativeStatus.Completed, response.Data.Status);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.InProgress, stored.Status);
    }

    [Fact]
    public async Task UpdateMilestoneProgress_ZeroPercent_DoesNotStartTheInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.NotStarted);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesUpdateProgress);
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        await handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 0
                }
            },
            CancellationToken.None);

        // Nothing has been reported as done, so the initiative is not started.
        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.NotStarted, stored.Status);

        var logs = await lease.Context.InitiativeUpdateLogs.AsNoTracking().ToListAsync();
        Assert.Single(logs);
        Assert.Equal("ProgressUpdated", Assert.Single(logs).ChangeType);
    }

    [Fact]
    public async Task UpdateMilestoneProgress_OnHoldInitiative_IsNotAutomaticallyRestarted()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.OnHold);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesUpdateProgress);
        var handler = new UpdateMilestoneProgressCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory(),
            user);

        await handler.Handle(
            new UpdateMilestoneProgressCommandRequest
            {
                Dto = new UpdateMilestoneProgressDto
                {
                    InitiativeId = initiative.Id,
                    MilestoneId = milestone.Id,
                    CompletionPercentage = 40
                }
            },
            CancellationToken.None);

        // A deliberate hold is a recorded decision and is not undone by a progress edit.
        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.OnHold, stored.Status);

        var logs = await lease.Context.InitiativeUpdateLogs.AsNoTracking().ToListAsync();
        Assert.Single(logs);
        Assert.Equal("ProgressUpdated", Assert.Single(logs).ChangeType);
    }

    // ------------------------------------------------------------------ FR-405 closure

    [Fact]
    public async Task Close_RecordsClosureInformationAndLocksTheInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var closer = await SeedUserAsync(lease, "Closer");
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = new TestCurrentUserService
        {
            UserId = closer.Id,
            UserName = "closer",
            Roles = new[] { "PowerUser" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesClose }
        };

        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var response = await handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    FinalActualExpenditure = 940m,
                    OutcomeSummary = "Delivered on time.",
                    LessonsLearned = "Lock the vendor earlier."
                }
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.Data.IsClosed);
        Assert.Equal(InitiativeStatus.Completed, response.Data.Status);
        Assert.Equal(60m, response.Data.BudgetPosition.BudgetVariance);
        Assert.Equal(94m, response.Data.BudgetPosition.BudgetUtilizationPercent);

        var stored = await lease.Context.Initiatives.SingleAsync();
        Assert.True(stored.IsClosed);
        Assert.NotNull(stored.ClosedAtUtc);
        Assert.Equal(closer.Id, stored.ClosedByUserId);
        Assert.Equal("Delivered on time.", stored.OutcomeSummary);

        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "InitiativeClosed");
    }

    // ------------------------------------------------------- FRD lifecycle: initiative statuses

    /// <summary>
    /// The FRD lists Not Started / In Progress / Completed / On Hold / Cancelled and no longer
    /// carries a separate Planned or Archived state. Guarded as an enum contract test so a future
    /// addition of one of the retired members fails here rather than silently reintroducing it.
    /// </summary>
    [Fact]
    public void InitiativeStatus_CarriesExactlyTheFrdStates()
    {
        var names = Enum.GetNames<InitiativeStatus>()
            .OrderBy(n => (int)Enum.Parse<InitiativeStatus>(n))
            .ToArray();

        Assert.Equal(new[] { "NotStarted", "InProgress", "Completed", "OnHold", "Cancelled" }, names);
    }

    /// <summary>Milestones carry the three FRD delivery states and no On Hold.</summary>
    [Fact]
    public void MilestoneStatus_CarriesExactlyTheFrdStates()
    {
        var names = Enum.GetNames<MilestoneStatus>()
            .OrderBy(n => (int)Enum.Parse<MilestoneStatus>(n))
            .ToArray();

        Assert.Equal(new[] { "NotStarted", "InProgress", "Completed" }, names);
    }

    /// <summary>
    /// A new initiative starts at Not Started. The old default was Planned, which no longer exists.
    /// </summary>
    [Fact]
    public async Task Create_DefaultsToNotStarted()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");

        var handler = new CreateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner)),
            BuildFactory());

        var response = await handler.Handle(
            new CreateInitiativeCommandRequest
            {
                Name = $"Initiative {Guid.NewGuid():N}"[..20],
                OwnerId = owner.Id,
                StartDate = DateTime.Today.AddDays(10),
                EndDate = DateTime.Today.AddDays(40)
            },
            CancellationToken.None);

        Assert.Equal(InitiativeStatus.NotStarted, response.Data.Status);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.NotStarted, stored.Status);
    }

    /// <summary>Completion is a recorded decision, so an initiative cannot be created as completed.</summary>
    [Fact]
    public async Task Create_RejectsStartingAnInitiativeAsCompleted()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");

        var handler = new CreateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner)),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateInitiativeCommandRequest
            {
                Name = $"Initiative {Guid.NewGuid():N}"[..20],
                OwnerId = owner.Id,
                StartDate = DateTime.Today.AddDays(10),
                EndDate = DateTime.Today.AddDays(40),
                Status = InitiativeStatus.Completed
            },
            CancellationToken.None));

        Assert.Contains("closure", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An initiative's execution window is part of its plan, so both dates are required on create.</summary>
    [Fact]
    public async Task Create_RequiresStartAndEndDate()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");

        var handler = new CreateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner)),
            BuildFactory());

        var missingStart = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateInitiativeCommandRequest
            {
                Name = $"Initiative {Guid.NewGuid():N}"[..20],
                OwnerId = owner.Id,
                EndDate = new DateTime(2026, 12, 31)
            },
            CancellationToken.None));
        Assert.Contains("start date", missingStart.Message, StringComparison.OrdinalIgnoreCase);

        var missingEnd = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateInitiativeCommandRequest
            {
                Name = $"Initiative {Guid.NewGuid():N}"[..20],
                OwnerId = owner.Id,
                StartDate = new DateTime(2026, 1, 1)
            },
            CancellationToken.None));
        Assert.Contains("end date", missingEnd.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>FR-401: a new initiative may start today; the boundary is inclusive.</summary>
    [Fact]
    public async Task Create_AllowsAStartDateOfToday()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");

        var handler = new CreateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner)),
            BuildFactory());

        var response = await handler.Handle(
            new CreateInitiativeCommandRequest
            {
                Name = $"Initiative {Guid.NewGuid():N}"[..20],
                OwnerId = owner.Id,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(30)
            },
            CancellationToken.None);

        Assert.True(response.Success);
    }

    /// <summary>FR-401 no-backdating rule: a start date before today is refused.</summary>
    [Fact]
    public async Task Create_RejectsAStartDateBeforeToday()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");

        var handler = new CreateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner)),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateInitiativeCommandRequest
            {
                Name = $"Initiative {Guid.NewGuid():N}"[..20],
                OwnerId = owner.Id,
                StartDate = DateTime.Today.AddDays(-1),
                EndDate = DateTime.Today.AddDays(30)
            },
            CancellationToken.None));

        Assert.Contains("start date", exception.Message, StringComparison.OrdinalIgnoreCase);

        // A refused create must not leave a partially written row behind.
        Assert.Empty(await lease.Context.Initiatives.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// FR-401: editing an initiative whose recorded start date is already in the past must stay
    /// possible — the no-backdating rule never applies to an unchanged historical start date.
    /// </summary>
    [Fact]
    public async Task Update_AllowsUnrelatedEditsWhenTheStartDateIsInThePast()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(
            lease, owner,
            startDate: DateTime.Today.AddDays(-30),
            endDate: DateTime.Today.AddDays(30));

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)),
            BuildFactory());

        var response = await handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                Description = "Corrected wording"
            },
            CancellationToken.None);

        Assert.True(response.Success);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal("Corrected wording", stored.Description);
        Assert.Equal(initiative.StartDate.Date, stored.StartDate.Date);
    }

    /// <summary>FR-401 no-backdating rule: moving an initiative's start date into the past is refused.</summary>
    [Fact]
    public async Task Update_RejectsMovingTheStartDateIntoThePast()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(
            lease, owner,
            startDate: DateTime.Today.AddDays(10),
            endDate: DateTime.Today.AddDays(60));

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                StartDate = DateTime.Today.AddDays(-1),
                EndDate = DateTime.Today.AddDays(60)
            },
            CancellationToken.None));

        Assert.Contains("start date", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>FR-401: rescheduling an initiative's start date into the future is allowed.</summary>
    [Fact]
    public async Task Update_AllowsMovingTheStartDateForward()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(
            lease, owner,
            startDate: DateTime.Today.AddDays(10),
            endDate: DateTime.Today.AddDays(60));

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)),
            BuildFactory());

        var newStart = DateTime.Today.AddDays(20);
        var response = await handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                StartDate = newStart,
                EndDate = DateTime.Today.AddDays(80)
            },
            CancellationToken.None);

        Assert.True(response.Success);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(newStart.Date, stored.StartDate.Date);
    }

    /// <summary>
    /// Status is the initiative's execution state and is only ever moved through the dedicated
    /// lifecycle actions (hold, resume, cancel, close, reopen). A bare detail edit can never flip
    /// it, so explicitly setting it through the update endpoint is refused.
    /// </summary>
    [Fact]
    public async Task Update_RefusesDirectStatusChanges()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                Status = InitiativeStatus.Completed
            },
            CancellationToken.None));

        Assert.Contains("lifecycle", exception.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.InProgress, stored.Status);
    }

    /// <summary>No lifecycle state may be authored through a plain status edit.</summary>
    [Theory]
    [InlineData(InitiativeStatus.NotStarted)]
    [InlineData(InitiativeStatus.InProgress)]
    [InlineData(InitiativeStatus.OnHold)]
    [InlineData(InitiativeStatus.Cancelled)]
    public async Task Update_RefusesAllLifecycleStatusesThroughTheStatusField(InitiativeStatus requested)
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)),
            BuildFactory());

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateInitiativeCommandRequest { Id = initiative.Id, Status = requested },
            CancellationToken.None));

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.InProgress, stored.Status);
    }

    // -------------------------------------------- FRD lifecycle: hold / resume / cancel actions

    /// <summary>
    /// Holding is a deliberate decision, so the reason is required and recorded in the audit trail
    /// together with the old and new status. The owner may hold their own initiative.
    /// </summary>
    [Fact]
    public async Task Hold_RequiresAReasonAndRecordsItAuditably()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);

        var handler = new PutInitiativeOnHoldCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            Owner(owner),
            BuildGuard(lease, Owner(owner)));

        var missingReason = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new PutInitiativeOnHoldCommandRequest { InitiativeId = initiative.Id },
            CancellationToken.None));
        Assert.Contains("reason", missingReason.Message, StringComparison.OrdinalIgnoreCase);

        var response = await handler.Handle(
            new PutInitiativeOnHoldCommandRequest
            {
                InitiativeId = initiative.Id,
                Reason = "Vendor resources reallocated"
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(InitiativeStatus.OnHold, response.Data.Status);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.OnHold, stored.Status);

        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "InitiativeHeld"
                && l.PreviousValue == "InProgress"
                && l.NewValue == "OnHold"
                && l.Details!.Contains("Vendor resources reallocated", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Only pre-closure execution states can move On Hold; a cancelled one cannot.</summary>
    [Fact]
    public async Task Hold_IsRefusedWhenTheTransitionIsInvalid()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Cancelled);

        var handler = new PutInitiativeOnHoldCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            Owner(owner, Domain.Constants.Permissions.InitiativesEdit),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new PutInitiativeOnHoldCommandRequest { InitiativeId = initiative.Id, Reason = "Revisit later" },
            CancellationToken.None));

        Assert.Contains("cannot be moved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Holding is refused for a user who is neither the owner nor an edit/close holder.</summary>
    [Fact]
    public async Task Hold_RefusesAUserWhoIsNeitherOwnerNorPowerUser()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var stranger = await SeedUserAsync(lease, "Someone Else");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);

        var strangerService = Owner(stranger, Domain.Constants.Permissions.InitiativesView);
        var handler = new PutInitiativeOnHoldCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            strangerService,
            BuildGuard(lease, strangerService));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new PutInitiativeOnHoldCommandRequest { InitiativeId = initiative.Id, Reason = "Suspending" },
            CancellationToken.None));
    }

    /// <summary>Resuming only applies to an initiative that is on hold; the owner may do it.</summary>
    [Fact]
    public async Task Resume_RequiresTheOnHoldState()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var holding = await SeedInitiativeAsync(lease, owner, InitiativeStatus.OnHold);
        var underway = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);

        var handler = new ResumeInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, Owner(owner)));

        var response = await handler.Handle(
            new ResumeInitiativeCommandRequest { InitiativeId = holding.Id },
            CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal(InitiativeStatus.InProgress, response.Data.Status);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new ResumeInitiativeCommandRequest { InitiativeId = underway.Id },
            CancellationToken.None));
        Assert.Contains("cannot be moved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cancelling is a structural decision reserved for edit/close holders (the owner-request
    /// workflow is an open decision), requires a reason which lands in the audit trail.
    /// </summary>
    [Fact]
    public async Task Cancel_RequiresAReasonAndIsReservedForEditOrCloseHolders()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.NotStarted);

        var closeUser = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            UserName = "closer",
            Roles = new[] { "PowerUser" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesClose }
        };
        var handler = new CancelInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, closeUser));

        var missingReason = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CancelInitiativeCommandRequest { InitiativeId = initiative.Id },
            CancellationToken.None));
        Assert.Contains("reason", missingReason.Message, StringComparison.OrdinalIgnoreCase);

        // An owner without the edit/close permissions cannot cancel their own initiative yet.
        var ownerService = Owner(owner);
        var ownerHandler = new CancelInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, ownerService));
        await Assert.ThrowsAsync<ForbiddenException>(() => ownerHandler.Handle(
            new CancelInitiativeCommandRequest { InitiativeId = initiative.Id, Reason = "Scope dropped" },
            CancellationToken.None));

        var response = await handler.Handle(
            new CancelInitiativeCommandRequest { InitiativeId = initiative.Id, Reason = "Scope dropped" },
            CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal(InitiativeStatus.Cancelled, response.Data.Status);

        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "InitiativeCancelled"
                && l.PreviousValue == "NotStarted"
                && l.NewValue == "Cancelled");
    }

    // ------------------------------------------------------------- FRD lifecycle: reopening

    /// <summary>Reopening records the reason and previous status; the closure history is preserved.</summary>
    [Fact]
    public async Task Reopen_RequiresAReasonAndRecordsPreviousStatus()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var closer = await SeedUserAsync(lease, "Closer");
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);
        initiative.IsClosed = true;
        initiative.Status = InitiativeStatus.Completed;
        initiative.ClosedAtUtc = DateTime.UtcNow;
        initiative.ClosedByUserId = closer.Id;
        await lease.Context.InitiativeUpdateLogs.AddAsync(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeClosed",
            Details = "Initiative closed."
        });
        await lease.Context.SaveChangesAsync();

        var handler = new ReopenInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory());

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new ReopenInitiativeCommandRequest { InitiativeId = initiative.Id },
            CancellationToken.None));

        var response = await handler.Handle(
            new ReopenInitiativeCommandRequest
            {
                InitiativeId = initiative.Id,
                Reason = "Actual expenditure was mis-recorded"
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.Data.IsClosed);
        Assert.Equal(InitiativeStatus.InProgress, response.Data.Status);

        var logs = await lease.Context.InitiativeUpdateLogs.ToListAsync();
        // The original closure row is untouched.
        Assert.Contains(logs, l => l.ChangeType == "InitiativeClosed");
        Assert.Contains(
            logs,
            l => l.ChangeType == "InitiativeReopened"
                && l.PreviousValue == "Completed"
                && l.NewValue == "InProgress"
                && l.Details!.Contains("mis-recorded", StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------------------- FR-401 strategic links on update

    /// <summary>
    /// Frontends that render every strategic select send StrategicLinksSpecified with the ids in
    /// the form: unchanged links are sent as-is, emptied selects arrive as null and clear that link,
    /// and the whole set is validated before it is written.
    /// </summary>
    [Fact]
    public async Task Update_StrategicLinksSpecifiedReplacesTheLinksExactly()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");

        // A real Goal → KRA → KPI hierarchy so the ids satisfy the foreign keys.
        var fiscalYear = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "FY2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31)
        };
        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Corporate", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow revenue", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Digital channels", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Online service uptake",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);

        var initiative = await SeedInitiativeAsync(lease, owner);
        initiative.GoalId = goal.Id;
        initiative.KraId = kra.Id;
        initiative.KpiId = kpi.Id;
        await lease.Context.SaveChangesAsync();

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner, Domain.Constants.Permissions.InitiativesEdit)),
            BuildFactory());

        var response = await handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                StrategicLinksSpecified = true,
                GoalId = null,
                KraId = null,
                KpiId = null
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Null(response.Data.GoalId);
        Assert.Null(response.Data.KraId);
        Assert.Null(response.Data.KpiId);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Null(stored.GoalId);
        Assert.Null(stored.KraId);
        Assert.Null(stored.KpiId);
    }

    // -------------------------------------------------- FRD lifecycle: expenditure authorization

    /// <summary>The initiative owner may post actual expenditure against their own initiative.</summary>
    [Fact]
    public async Task Update_AllowsTheOwnerToPostActualExpenditure()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        // No permission at all: the owner check alone has to carry this.
        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(owner)),
            BuildFactory());

        await handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                ActualExpenditure = 250m
            },
            CancellationToken.None);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(250m, stored.ActualExpenditure);

        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "BudgetUpdated");
    }

    /// <summary>
    /// Someone who is neither the owner nor a Power User cannot post spend against this initiative,
    /// and the refusal happens before anything is written.
    /// </summary>
    [Fact]
    public async Task Update_RefusesActualExpenditureFromAnUnauthorisedUser()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var stranger = await SeedUserAsync(lease, "Someone Else");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var handler = new UpdateInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, Owner(stranger, Domain.Constants.Permissions.InitiativesUpdateProgress)),
            BuildFactory());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new UpdateInitiativeCommandRequest
            {
                Id = initiative.Id,
                Name = "Renamed by the intruder",
                ActualExpenditure = 999m
            },
            CancellationToken.None));

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(0m, stored.ActualExpenditure);
        Assert.NotEqual("Renamed by the intruder", stored.Name);
    }

    // ------------------------------------------ FRD responsibility matrix: narrow expenditure path

    private static UpdateInitiativeExpenditureCommandHandler BuildExpenditureHandler(
        TestDbContextLease lease,
        TestCurrentUserService user) => new(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

    /// <summary>
    /// The owner reaches the expenditure endpoint holding view only - no edit permission - and posts
    /// spend without gaining any detail-editing rights. The write is audited like every other
    /// budget change.
    /// </summary>
    [Fact]
    public async Task UpdateExpenditure_AllowsTheAssignedOwnerWhoHoldsNoEditPermission()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        initiative.ActualExpenditure = 100m;
        await lease.Context.SaveChangesAsync();

        var handler = BuildExpenditureHandler(lease, Owner(owner, Domain.Constants.Permissions.InitiativesView));

        var response = await handler.Handle(
            new UpdateInitiativeExpenditureCommandRequest { Id = initiative.Id, ActualExpenditure = 250m },
            CancellationToken.None);

        Assert.True(response.Success);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(250m, stored.ActualExpenditure);

        var log = Assert.Single(
            await lease.Context.InitiativeUpdateLogs.AsNoTracking().Where(l => l.ChangeType == "BudgetUpdated").ToListAsync());
        Assert.Equal("100.00", log.PreviousValue);
        Assert.Equal("250.00", log.NewValue);
    }

    /// <summary>A Power User who is not the owner still posts spend through the expenditure permission.</summary>
    [Fact]
    public async Task UpdateExpenditure_AllowsAPowerUserWhoIsNotTheOwner()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var powerUser = await SeedUserAsync(lease, "Power User");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var handler = BuildExpenditureHandler(
            lease, Owner(powerUser, Domain.Constants.Permissions.InitiativesUpdateExpenditure));

        await handler.Handle(
            new UpdateInitiativeExpenditureCommandRequest { Id = initiative.Id, ActualExpenditure = 40m },
            CancellationToken.None);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(40m, stored.ActualExpenditure);
    }

    /// <summary>
    /// Holding view (the policy the endpoint is gated on) is not enough for somebody who owns
    /// nothing here: the guard, not the route, is what refuses them.
    /// </summary>
    [Fact]
    public async Task UpdateExpenditure_RefusesANonOwnerWhoHoldsOnlyView()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var stranger = await SeedUserAsync(lease, "Someone Else");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var handler = BuildExpenditureHandler(lease, Owner(stranger, Domain.Constants.Permissions.InitiativesView));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new UpdateInitiativeExpenditureCommandRequest { Id = initiative.Id, ActualExpenditure = 999m },
            CancellationToken.None));

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(0m, stored.ActualExpenditure);
        Assert.Empty(await lease.Context.InitiativeUpdateLogs.ToListAsync());
    }

    /// <summary>The FR-405 closed lock applies to the owner too: spend on a closed initiative is fixed.</summary>
    [Fact]
    public async Task UpdateExpenditure_KeepsAClosedInitiativeReadOnlyForItsOwner()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Completed);
        initiative.IsClosed = true;
        initiative.ActualExpenditure = 300m;
        await lease.Context.SaveChangesAsync();

        var handler = BuildExpenditureHandler(lease, Owner(owner, Domain.Constants.Permissions.InitiativesView));

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateInitiativeExpenditureCommandRequest { Id = initiative.Id, ActualExpenditure = 999m },
            CancellationToken.None));

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(300m, stored.ActualExpenditure);
    }

    /// <summary>Spend follows the existing budget rule: never negative.</summary>
    [Fact]
    public async Task UpdateExpenditure_RejectsNegativeSpend()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var handler = BuildExpenditureHandler(lease, Owner(owner, Domain.Constants.Permissions.InitiativesView));

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateInitiativeExpenditureCommandRequest { Id = initiative.Id, ActualExpenditure = -1m },
            CancellationToken.None));

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(0m, stored.ActualExpenditure);
    }

    // -------------------------------------------------- FRD lifecycle: milestone delivery states

    /// <summary>
    /// Milestone status follows completion: 100% completes it, 0% has not started, and anything in
    /// between is under way even when a stale Not Started is submitted.
    /// </summary>
    [Theory]
    [InlineData(100, MilestoneStatus.NotStarted, MilestoneStatus.Completed)]
    [InlineData(100, MilestoneStatus.Completed, MilestoneStatus.Completed)]
    [InlineData(0, MilestoneStatus.InProgress, MilestoneStatus.NotStarted)]
    [InlineData(0, MilestoneStatus.NotStarted, MilestoneStatus.NotStarted)]
    [InlineData(40, MilestoneStatus.NotStarted, MilestoneStatus.InProgress)]
    [InlineData(40, MilestoneStatus.InProgress, MilestoneStatus.InProgress)]
    public void ResolveMilestoneStatus_DerivesMilestoneStatusFromCompletion(
        int completion,
        MilestoneStatus requested,
        MilestoneStatus expected)
    {
        Assert.Equal(expected, InitiativeGuard.ResolveMilestoneStatus(completion, requested));
    }

    /// <summary>Readiness is false until the plan is configured and delivery reaches 100%.</summary>
    [Fact]
    public void IsReadyForCompletion_RequiresAConfiguredPlanAndFullDelivery()
    {
        var unconfigured = new Domain.Calculation.InitiativeProgressResult(100m, 60m, true, false, Array.Empty<Domain.Calculation.MilestoneContribution>());
        var partial = new Domain.Calculation.InitiativeProgressResult(60m, 100m, true, true, Array.Empty<Domain.Calculation.MilestoneContribution>());
        var noMilestones = new Domain.Calculation.InitiativeProgressResult(0m, 0m, false, true, Array.Empty<Domain.Calculation.MilestoneContribution>());
        var delivered = new Domain.Calculation.InitiativeProgressResult(100m, 100m, true, true, Array.Empty<Domain.Calculation.MilestoneContribution>());

        Assert.False(InitiativeGuard.IsReadyForCompletion(unconfigured));
        Assert.False(InitiativeGuard.IsReadyForCompletion(partial));
        Assert.False(InitiativeGuard.IsReadyForCompletion(noMilestones));
        Assert.True(InitiativeGuard.IsReadyForCompletion(delivered));
    }

    /// <summary>
    /// The closure gate refuses an initiative with no milestones: there is no delivery progress to
    /// have completed, so a 0% figure cannot be read as finished.
    /// </summary>
    [Fact]
    public async Task Close_RejectsAnInitiativeWithNoMilestones()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var closer = await SeedUserAsync(lease, "Closer");
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var user = new TestCurrentUserService
        {
            UserId = closer.Id,
            UserName = "closer",
            Roles = new[] { "PowerUser" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesClose }
        };

        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    OutcomeSummary = "Done."
                }
            },
            CancellationToken.None));

        Assert.Contains("no milestones", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Completion is confirmed by an authorised user only. The controller already filters on the close
    /// permission, but the rule is enforced in the handler so it cannot be bypassed.
    /// </summary>
    [Fact]
    public async Task Close_RefusesAUserWhoIsNeitherOwnerNorPowerUser()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var stranger = await SeedUserAsync(lease, "Someone Else");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = new TestCurrentUserService
        {
            UserId = stranger.Id,
            UserName = "stranger",
            Roles = new[] { "DivisionAccount" },
            Permissions = Array.Empty<string>()
        };

        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    OutcomeSummary = "Done."
                }
            },
            CancellationToken.None));

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.False(stored.IsClosed);
    }

    /// <summary>
    /// The owner can confirm completion of their own initiative without holding the close
    /// permission, which is the "authorized initiative owner" half of the FRD rule.
    /// </summary>
    [Fact]
    public async Task Close_AllowsTheOwnerToConfirmCompletion()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner);
        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var response = await handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    OutcomeSummary = "Delivered."
                }
            },
            CancellationToken.None);

        Assert.True(response.Data.IsClosed);
        Assert.Equal(InitiativeStatus.Completed, response.Data.Status);
    }

    /// <summary>
    /// A final expenditure that differs from the expenditure recorded to date is a budget
    /// correction and gets its own audit entry, not just the closure record.
    /// </summary>
    [Fact]
    public async Task Close_RecordsAFinalExpenditureCorrectionInTheAuditTrail()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesClose);
        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        await handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    FinalActualExpenditure = 880m,
                    OutcomeSummary = "Delivered."
                }
            },
            CancellationToken.None);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(880m, stored.ActualExpenditure);

        var logs = await lease.Context.InitiativeUpdateLogs.AsNoTracking().ToListAsync();
        Assert.Contains(logs, l => l.ChangeType == "InitiativeClosed");
        Assert.Contains(logs, l => l.ChangeType == "BudgetUpdated" && l.PreviousValue == "0.00");
    }

    [Fact]
    public async Task Close_RejectsIncompleteProgress()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var closer = await SeedUserAsync(lease, "Closer");
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 60);

        var user = new TestCurrentUserService
        {
            UserId = closer.Id,
            UserName = "closer",
            Roles = new[] { "PowerUser" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesClose }
        };

        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    OutcomeSummary = "Done."
                }
            },
            CancellationToken.None));

        Assert.Contains("60", exception.Message);
    }

    [Fact]
    public async Task Close_RejectsWhenMilestoneWeightsAreNotConfigured()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var closer = await SeedUserAsync(lease, "Closer");
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        // Weights total 90, so a "100%" progress figure is not trustworthy enough to close on.
        await SeedMilestoneAsync(lease, initiative, 90m, 100);

        var user = new TestCurrentUserService
        {
            UserId = closer.Id,
            UserName = "closer",
            Roles = new[] { "PowerUser" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesClose }
        };

        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    OutcomeSummary = "Done."
                }
            },
            CancellationToken.None));

        Assert.Contains("total 100", exception.Message);
    }

    [Fact]
    public async Task Close_AllowsClosureWithoutOutcomeSummary()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var closer = await SeedUserAsync(lease, "Closer");
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = new TestCurrentUserService
        {
            UserId = closer.Id,
            UserName = "closer",
            Roles = new[] { "PowerUser" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesClose }
        };

        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var response = await handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = new DateTime(2026, 10, 5),
                    LessonsLearned = "Lock the vendor earlier."
                }
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.Data.IsClosed);
        Assert.Null(response.Data.OutcomeSummary);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Null(stored.OutcomeSummary);
        Assert.Equal("Lock the vendor earlier.", stored.LessonsLearned);
    }

    /// <summary>
    /// FR-405: the actual end date records the day delivery actually finished, so it can never be
    /// dated in the future. The frontend restricts the picker, the validator rejects it up front,
    /// and the handler enforces it as the final gate.
    /// </summary>
    [Fact]
    public async Task Close_RejectsAnActualEndDateInTheFuture()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesClose);
        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = DateTime.Today.AddDays(3),
                    OutcomeSummary = "Delivered."
                }
            },
            CancellationToken.None));

        Assert.Contains("less than or equal to today", exception.Message);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.False(stored.IsClosed);
    }

    /// <summary>
    /// FR-405: an initiative must have run for at least one day, so the actual end date has to be
    /// strictly later than the start date — an end date on the same day is not a completed delivery.
    /// </summary>
    [Fact]
    public async Task Close_RejectsAnActualEndDateEqualToTheStartDate()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesClose);
        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = initiative.StartDate,
                    OutcomeSummary = "Delivered."
                }
            },
            CancellationToken.None));

        Assert.Contains("strictly later", exception.Message);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.False(stored.IsClosed);
    }

    /// <summary>
    /// FR-405: the recorded actual end date must sit inside the initiative window. When delivery runs
    /// past the planned end date the closure is rejected rather than silently recorded outside the
    /// agreed window.
    /// </summary>
    [Fact]
    public async Task Close_RejectsAnActualEndDateAfterThePlannedEndDate()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(
            lease,
            owner,
            startDate: DateTime.Today.AddDays(-30),
            endDate: DateTime.Today.AddDays(-5));
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesClose);
        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = DateTime.Today.AddDays(-1),
                    OutcomeSummary = "Delivered."
                }
            },
            CancellationToken.None));

        Assert.Contains("on or before the initiative's planned End Date", exception.Message);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.False(stored.IsClosed);
    }

    /// <summary>
    /// FR-405: the planned end date itself is inside the window, so closing exactly on it is allowed.
    /// </summary>
    [Fact]
    public async Task Close_AcceptsAnActualEndDateOnThePlannedEndDate()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(
            lease,
            owner,
            startDate: DateTime.Today.AddDays(-30),
            endDate: DateTime.Today);
        await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesClose);
        var handler = new CloseInitiativeCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            new InitiativeProgressCalculator(),
            user,
            BuildGuard(lease, user));

        var response = await handler.Handle(
            new CloseInitiativeCommandRequest
            {
                Dto = new CloseInitiativeDto
                {
                    InitiativeId = initiative.Id,
                    ActualEndDate = DateTime.Today,
                    OutcomeSummary = "Delivered."
                }
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.Data.IsClosed);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.True(stored.IsClosed);
        Assert.Equal(DateTime.Today, stored.ActualEndDate!.Value.Date);
    }

    [Fact]
    public async Task ClosedInitiative_RejectsMilestoneEditsFromOrdinaryUsers()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Completed);
        initiative.IsClosed = true;
        await lease.Context.SaveChangesAsync();

        // The owner has milestone permission but the initiative is closed, so the write is refused.
        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var guard = BuildGuard(lease, user);

        var exception = Assert.Throws<BadRequestException>(() => guard.EnsureMutable(initiative));
        Assert.Contains("read-only", exception.Message);
    }

    [Fact]
    public async Task ClosedInitiative_AllowsAdminCorrections()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Completed);
        initiative.IsClosed = true;
        await lease.Context.SaveChangesAsync();

        var admin = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            UserName = "admin",
            Roles = new[] { "Admin" },
            Permissions = new[] { Domain.Constants.Permissions.InitiativesEdit }
        };

        // FR-405: Admin and Power User retain correction rights on closed initiatives.
        BuildGuard(lease, admin).EnsureMutable(initiative);
    }

    // ------------------------------------------------------------------ dependencies

    [Fact]
    public async Task AddDependency_LinksInitiativeToItsPrerequisite()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var prerequisite = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageDependencies);
        var handler = new AddInitiativeDependencyCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeDependencyRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, user));

        var response = await handler.Handle(
            new AddInitiativeDependencyCommandRequest
            {
                InitiativeId = initiative.Id,
                DependsOnInitiativeId = prerequisite.Id
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(response.Data.Dependencies);
        Assert.Equal(prerequisite.Id, response.Data.Dependencies[0].DependsOnInitiativeId);
        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "DependencyAdded");
    }

    [Fact]
    public async Task AddDependency_RejectsSelfReference()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageDependencies);
        var handler = new AddInitiativeDependencyCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeDependencyRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, user));

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new AddInitiativeDependencyCommandRequest
            {
                InitiativeId = initiative.Id,
                DependsOnInitiativeId = initiative.Id
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task AddDependency_RejectsCycle()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var first = await SeedInitiativeAsync(lease, owner);
        var second = await SeedInitiativeAsync(lease, owner);
        var third = await SeedInitiativeAsync(lease, owner);

        // second depends on third, and third depends on first. Asking first -> second would close
        // the loop first -> second -> third -> first.
lease.Context.InitiativeDependencies.AddRange(
            new InitiativeDependency
            {
                Id = Guid.NewGuid(),
                InitiativeId = second.Id,
                DependsOnInitiativeId = third.Id
            },
            new InitiativeDependency
            {
                Id = Guid.NewGuid(),
                InitiativeId = third.Id,
                DependsOnInitiativeId = first.Id
            });
        await lease.Context.SaveChangesAsync();

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageDependencies);
        var handler = new AddInitiativeDependencyCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeDependencyRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, user));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new AddInitiativeDependencyCommandRequest
            {
                InitiativeId = first.Id,
                DependsOnInitiativeId = second.Id
            },
            CancellationToken.None));

        Assert.Contains("circular", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddDependency_RejectsDuplicate()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var prerequisite = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageDependencies);
        var handler = new AddInitiativeDependencyCommandHandler(
            new InitiativeRepository(lease.Context),
            new InitiativeDependencyRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildFactory(),
            BuildGuard(lease, user));

        var request = new AddInitiativeDependencyCommandRequest
        {
            InitiativeId = initiative.Id,
            DependsOnInitiativeId = prerequisite.Id
        };

        await handler.Handle(request, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public void CycleDetection_WalksTransitivePrerequisites()
    {
var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var standalone = Guid.NewGuid();

        var edges = new List<(Guid, Guid)>
        {
            (b, c),
            (c, a)
        };

        // a -> b would close a -> b -> c -> a.
        Assert.True(AddInitiativeDependencyCommandHandler.WouldCreateCycle(edges, a, b));
        // c -> b is a direct two-way pair, so it is a cycle too.
        Assert.True(AddInitiativeDependencyCommandHandler.WouldCreateCycle(edges, c, b));
        // An unrelated initiative can depend on b freely: b's prerequisites never reach it.
        Assert.False(AddInitiativeDependencyCommandHandler.WouldCreateCycle(edges, standalone, b));
        // b -> a is fine: nothing in a's prerequisite chain leads back to b.
        Assert.False(AddInitiativeDependencyCommandHandler.WouldCreateCycle(edges, b, a));
    }

    // ------------------------------------------------------------------ milestone rules

    [Fact]
    public void ValidateMilestoneRanges_RejectsOutOfBoundsValues()
    {
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateMilestoneRanges(-1m, 0));
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateMilestoneRanges(10m, 101));
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateMilestoneRanges(10m, -1));

        // Boundaries are allowed.
        InitiativeGuard.ValidateMilestoneRanges(0m, 0);
        InitiativeGuard.ValidateMilestoneRanges(100m, 100);
    }

    [Fact]
    public void ValidateBudgetAndDates_RejectInvalidCombinations()
    {
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateBudget(-1m, 0m));
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateBudget(10m, -1m));
        InitiativeGuard.ValidateBudget(0m, 0m);

        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateDateRange(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 5, 31)));

        InitiativeGuard.ValidateDateRange(new DateTime(2026, 5, 31), new DateTime(2026, 6, 1));
        InitiativeGuard.ValidateDateRange(null, null);
    }

    [Fact]
    public async Task CreateMilestone_RejectsDueDateOutsideInitiativeWindow()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var guard = BuildGuard(lease, user);

        var exception = Assert.Throws<BadRequestException>(() =>
            InitiativeGuard.ValidateMilestoneDueDate(initiative, new DateTime(2027, 1, 1)));

        Assert.Contains("after the initiative end date", exception.Message);
    }

    /// <summary>
    /// FR-402: a milestone due date outside the initiative window is refused on the real create and
    /// update handlers, not just the guard, so the API cannot be used to bypass the rule.
    /// </summary>
    [Fact]
    public async Task CreateMilestone_RejectsADueDateAfterTheInitiativeEnds_ThroughTheHandler()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new CreateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        // The seeded initiative runs 2026-01-01 to 2026-12-31, so 2027 is out of window.
        var after = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Too late",
                Weight = 50m,
                CompletionPercentage = 0,
                DueDate = new DateTime(2027, 1, 1)
            },
            CancellationToken.None));
        Assert.Contains("after the initiative end date", after.Message);

        var before = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Too early",
                Weight = 50m,
                CompletionPercentage = 0,
                DueDate = new DateTime(2025, 12, 31)
            },
            CancellationToken.None));
        Assert.Contains("before the initiative start date", before.Message);

        // Nothing was persisted by either rejected call.
        Assert.Equal(0, await lease.Context.Milestones.CountAsync(m => m.InitiativeId == initiative.Id));

        // Both bounds are inclusive, so the boundary days themselves are accepted.
        foreach (var boundary in new[] { new DateTime(2026, 1, 1), new DateTime(2026, 12, 31) })
        {
            var response = await handler.Handle(
                new CreateMilestoneCommandRequest
                {
                    InitiativeId = initiative.Id,
                    Name = $"Boundary {boundary:yyyy-MM-dd}",
                    Weight = 10m,
                    CompletionPercentage = 0,
                    DueDate = boundary
                },
                CancellationToken.None);

            Assert.True(response.Success);
        }

        Assert.Equal(2, await lease.Context.Milestones.CountAsync(m => m.InitiativeId == initiative.Id));
    }

    /// <summary>
    /// The window check must also run when an existing milestone is moved outside it, otherwise the
    /// create path would be the only place the rule was honoured.
    /// </summary>
    [Fact]
    public async Task UpdateMilestone_RejectsADueDateOutsideTheInitiativeWindow()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        var milestone = await SeedMilestoneAsync(lease, initiative, 50m, 0, "Phase 1");

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new UpdateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id,
                Name = "Phase 1",
                DueDate = new DateTime(2027, 6, 1)
            },
            CancellationToken.None));

        Assert.Contains("after the initiative end date", exception.Message);

        // The stored due date is untouched by the rejected update.
        var stored = await lease.Context.Milestones.AsNoTracking().SingleAsync(m => m.Id == milestone.Id);
        Assert.Equal(new DateTime(2026, 6, 30), stored.DueDate);
    }

    /// <summary>
    /// The FRD progress bands are served from the backend so the initiative page, the dashboards
    /// and the reports cannot drift apart. A single 100%-weight milestone makes the weighted
    /// progress equal the completion figure, which keeps each boundary unambiguous.
    /// </summary>
    [Theory]
    [InlineData(100, DashboardRag.Green)]
    [InlineData(95, DashboardRag.Green)]
    [InlineData(94, DashboardRag.Amber)]
    [InlineData(70, DashboardRag.Amber)]
    [InlineData(69, DashboardRag.Red)]
    [InlineData(0, DashboardRag.Red)]
    public async Task BuildSummary_BandsProgressUsingTheFrdThresholds(int completion, string expected)
    {
        var initiative = NewInitiative();
        var milestone = NewMilestone(initiative.Id, 100m, completion);

        var dto = await BuildFactory().BuildSummaryAsync(initiative, new[] { milestone });

        Assert.Equal(completion, dto.ProgressPercentage);
        Assert.Equal(expected, dto.ProgressRagStatus);
    }

    /// <summary>
    /// Without milestones there is nothing to measure, so the initiative reports "no data" instead
    /// of being banded red for a progress figure that was never really calculated.
    /// </summary>
    [Fact]
    public async Task BuildSummary_ReportsNoRagBandWhenTheInitiativeHasNoMilestones()
    {
        var initiative = NewInitiative();

        var dto = await BuildFactory().BuildSummaryAsync(initiative, Array.Empty<Milestone>());

        Assert.Equal(DashboardRag.NoData, dto.ProgressRagStatus);
    }

    /// <summary>
    /// FR-607: stored thresholds must move the bands. 60% is Amber under the FR-607 defaults,
    /// but turns Green once an administrator relaxes the Green threshold to 80%.
    /// </summary>
    [Fact]
    public async Task BuildSummary_BandsProgressWithConfiguredThresholds()
    {
        var thresholds = new TestRagThresholdService
        {
            Current = new RagThresholds { Green = 60m, Yellow = 40m }
        };
        var initiative = NewInitiative();
        var milestone = NewMilestone(initiative.Id, 100m, 65);

        var dto = await BuildFactory(thresholds).BuildSummaryAsync(initiative, new[] { milestone });

        Assert.Equal(DashboardRag.Green, dto.ProgressRagStatus);
    }

    private static Initiative NewInitiative() => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Initiative {Guid.NewGuid():N}"[..20],
        OwnerId = Guid.NewGuid(),
        Priority = InitiativePriority.High,
        Status = InitiativeStatus.InProgress,
        Budget = 1_000m,
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2026, 12, 31),
        CreatedDate = DateTime.UtcNow
    };

    private static Milestone NewMilestone(Guid initiativeId, decimal weight, int completion) => new()
    {
        Id = Guid.NewGuid(),
        InitiativeId = initiativeId,
        Name = "Phase 1",
        Weight = weight,
        CompletionPercentage = completion,
        DueDate = new DateTime(2026, 6, 30),
        CreatedDate = DateTime.UtcNow
    };

    /// <summary>
    /// FR-402: milestone weights share one 100% budget. A weightless milestone contributes nothing
    /// to progress, so it is only refused once the other milestones already hold the whole budget.
    /// </summary>
    [Fact]
    public void ValidateMilestoneWeightBudget_RejectsAWeightlessMilestoneOnceTheBudgetIsFull()
    {
        // The budget is spent, so there is no share left to give a 0% milestone.
        var exception = Assert.Throws<BadRequestException>(
            () => InitiativeGuard.ValidateMilestoneWeightBudget(100m, 0m));
        Assert.Contains("never contribute to initiative progress", exception.Message);

        // Float drift must not be mistaken for spare budget.
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateMilestoneWeightBudget(99.99999m, 0m));

        // Below the cap there is room to plan, so a placeholder milestone is still allowed and the
        // user can allocate the remaining weight later.
        InitiativeGuard.ValidateMilestoneWeightBudget(90m, 0m);
        InitiativeGuard.ValidateMilestoneWeightBudget(0m, 0m);

        // Giving it a real share is the fix, so it must be accepted.
        InitiativeGuard.ValidateMilestoneWeightBudget(80m, 20m);
    }

    [Fact]
    public async Task CreateMilestone_RejectsAWeightlessMilestoneOnceTheBudgetIsFull()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        // 100% is already committed, so a 0% milestone would be silently ignored.
        await SeedMilestoneAsync(lease, initiative, 60m, 0, "Phase 1");
        await SeedMilestoneAsync(lease, initiative, 40m, 0, "Phase 2");

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new CreateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Phase 3",
                Weight = 0m,
                CompletionPercentage = 0,
                DueDate = new DateTime(2026, 6, 30)
            },
            CancellationToken.None));

        Assert.Contains("never contribute to initiative progress", exception.Message);

        // Nothing was written, so the rejected row cannot dilute the plan.
        Assert.Equal(100m, await new MilestoneRepository(lease.Context).GetTotalWeightAsync(initiative.Id));
        Assert.Equal(2, await lease.Context.Milestones.CountAsync(m => m.InitiativeId == initiative.Id));
    }

    [Fact]
    public void ValidateMilestoneWeightBudget_RejectsTotalsOverOneHundred()
    {
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateMilestoneWeightBudget(90m, 20m));
        Assert.Throws<BadRequestException>(() => InitiativeGuard.ValidateMilestoneWeightBudget(100m, 1m));

        // A plan may sit below 100% while it is still being built up, and 100% exactly is the
        // target the closure rule expects.
        InitiativeGuard.ValidateMilestoneWeightBudget(90m, 10m);
        InitiativeGuard.ValidateMilestoneWeightBudget(0m, 100m);
        InitiativeGuard.ValidateMilestoneWeightBudget(33.3333m, 33.3333m);
    }

    [Fact]
    public async Task CreateMilestone_RejectsWeightBeyondTheRemainingBudget()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        // 80% is already committed, so only 20% is left for this milestone.
        await SeedMilestoneAsync(lease, initiative, 80m, 0, "Phase 1");

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new CreateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Phase 2",
                Weight = 25m,
                CompletionPercentage = 0,
                DueDate = new DateTime(2026, 6, 30)
            },
            CancellationToken.None));

        Assert.Contains("105.00%", exception.Message);

        // Nothing was written, so the rejected milestone cannot leak into the weight total.
        Assert.Equal(80m, await new MilestoneRepository(lease.Context).GetTotalWeightAsync(initiative.Id));
    }

    [Fact]
    public async Task CreateMilestone_AcceptsWeightThatFillsTheRemainingBudget()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 80m, 0, "Phase 1");

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new CreateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var response = await handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Phase 2",
                Weight = 20m,
                CompletionPercentage = 0,
                DueDate = new DateTime(2026, 6, 30)
            },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(100m, await new MilestoneRepository(lease.Context).GetTotalWeightAsync(initiative.Id));
    }

    [Fact]
    public async Task UpdateMilestone_RejectsWeightThatOverspendsTheSharedBudget()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        await SeedMilestoneAsync(lease, initiative, 60m, 0, "Phase 1");
        var milestone = await SeedMilestoneAsync(lease, initiative, 30m, 0, "Phase 2");

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new UpdateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new UpdateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id,
                Weight = 50m
            },
            CancellationToken.None));

        Assert.Contains("110.00%", exception.Message);
        Assert.Equal(30m, (await new MilestoneRepository(lease.Context).GetByIdForInitiativeAsync(milestone.Id, initiative.Id))!.Weight);
    }

    [Fact]
    public async Task UpdateMilestone_AllowsAnUnchangedWeightOnAnAlreadyOverBudgetInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);
        // Legacy data that already totals 110%: an unrelated edit must not be blocked, otherwise
        // the record could never be corrected.
        await SeedMilestoneAsync(lease, initiative, 60m, 0, "Phase 1");
        var milestone = await SeedMilestoneAsync(lease, initiative, 50m, 0, "Phase 2");

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new UpdateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var response = await handler.Handle(
            new UpdateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id,
                Weight = 50m,
                CompletionPercentage = 25
            },
            CancellationToken.None);

        Assert.True(response.Success);
    }

    [Fact]
    public async Task CreateMilestone_LogsCreationAndSetsStatusFromCompletion()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
var handler = new CreateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        var response = await handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Phase 1",
                Weight = 100m,
                CompletionPercentage = 100,
                DueDate = new DateTime(2026, 6, 30),
                Comments = "Signed off."
            },
            CancellationToken.None);

Assert.True(response.Success);
        Assert.Equal(MilestoneStatus.Completed, response.Data.Status);
        Assert.Equal(100m, response.Data.Weight);

        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "MilestoneCreated");

        // FRD lifecycle: a milestone completing the plan makes the initiative *ready* for
        // completion, not completed. Only an authorised closure sets Completed.
        var stored = await lease.Context.Initiatives.SingleAsync();
        Assert.NotEqual(InitiativeStatus.Completed, stored.Status);

        var detail = await new InitiativeRepository(lease.Context).GetDetailAsync(initiative.Id);
        var projected = await BuildFactory().BuildDetailAsync(detail!);
        Assert.True(projected.IsReadyForCompletion);
        Assert.Null(projected.NotReadyReason);
    }

    [Fact]
    public async Task CreateMilestone_WithPartialWeight_DoesNotCompleteTheInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new CreateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        await handler.Handle(
            new CreateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Name = "Phase 1",
                Weight = 40m,
                CompletionPercentage = 100,
                DueDate = new DateTime(2026, 6, 30)
            },
            CancellationToken.None);

        var stored = await lease.Context.Initiatives.SingleAsync();
        Assert.NotEqual(InitiativeStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task UpdateMilestone_ReachingFullProgress_LeavesInitiativeStatusAlone()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.NotStarted);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new UpdateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        await handler.Handle(
            new UpdateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id,
                CompletionPercentage = 100
            },
            CancellationToken.None);

        // FRD lifecycle: progress and status are independent. 100% makes the initiative ready,
        // it does not complete it.
        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.NotEqual(InitiativeStatus.Completed, stored.Status);
        Assert.Equal(InitiativeStatus.NotStarted, stored.Status);

        var detail = await new InitiativeRepository(lease.Context).GetDetailAsync(initiative.Id);
        Assert.True((await BuildFactory().BuildDetailAsync(detail!)).IsReadyForCompletion);
    }

    [Fact]
    public async Task UpdateMilestone_PartialProgress_LeavesInitiativeStatusAlone()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.NotStarted);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 0);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new UpdateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        await handler.Handle(
            new UpdateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id,
                CompletionPercentage = 40
            },
            CancellationToken.None);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.NotStarted, stored.Status);
    }

    [Fact]
    public async Task UpdateMilestone_ReducingProgress_DoesNotReopenACompletedInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Completed);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new UpdateMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        await handler.Handle(
            new UpdateMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id,
                CompletionPercentage = 40
            },
            CancellationToken.None);

        // FRD lifecycle: a progress correction must not silently undo a recorded completion.
        // Reopening for correction is an explicit action.
        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task DeleteMilestone_ReducesProgressAndReopensTheInitiativeStatus()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Completed);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 100);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new DeleteMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        await handler.Handle(
            new DeleteMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id
            },
            CancellationToken.None);

        var stored = await lease.Context.Initiatives.AsNoTracking().SingleAsync();
        Assert.Equal(InitiativeStatus.Completed, stored.Status);

        Assert.Contains(
            await lease.Context.InitiativeUpdateLogs.ToListAsync(),
            l => l.ChangeType == "MilestoneDeleted");
    }

/// <summary>
    /// Delete must never remove a row. It flips CommonEntity.IsDeleted, which the global query filter
    /// (e => !e.IsDeleted) excludes from every read, so the milestone disappears from the UI while
    /// the row and its history survive in the database. Because the row survives, the FK to
    /// Milestones stays satisfied and the per-milestone history query keeps working - the previous
    /// implementation physically deleted the row and had to sever the FK by hand.
    /// </summary>
    [Fact]
    public async Task DeleteMilestone_KeepsTheRowAndItsHistoryAndHidesItFromQueries()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);
        var milestone = await SeedMilestoneAsync(lease, initiative, 100m, 50);

        lease.Context.InitiativeUpdateLogs.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            MilestoneId = milestone.Id,
            ChangeType = "MilestoneProgressUpdated",
            Details = "Completion moved to 50%.",
            PreviousValue = "0%",
            NewValue = "50%"
        });
        await lease.Context.SaveChangesAsync();

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new DeleteMilestoneCommandHandler(
            new InitiativeRepository(lease.Context),
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            BuildFactory());

        await handler.Handle(
            new DeleteMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = milestone.Id
            },
            CancellationToken.None);

        // Hidden from every normal read, which is what removes it from the UI.
        Assert.Empty(await lease.Context.Milestones.ToListAsync());

        // Still physically present, flagged deleted rather than removed.
        var stored = await lease.Context.Milestones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(milestone.Id, stored.Id);
        Assert.True(stored.IsDeleted);
        Assert.NotEqual(default, stored.UpdatedDate);

        // Milestone shadows CommonEntity.Status with its execution enum, so deactivating must not
        // touch the workflow status - it can only be IsDeleted here. The milestone was seeded at 50%
        // so its execution state is NotStarted, and that must survive untouched.
        Assert.Equal(MilestoneStatus.NotStarted, stored.Status);

        // The history keeps its milestone reference, so per-milestone history still resolves.
        var progressEntry = await lease.Context.InitiativeUpdateLogs
            .AsNoTracking()
            .SingleAsync(l => l.ChangeType == "MilestoneProgressUpdated");

        Assert.Equal(milestone.Id, progressEntry.MilestoneId);
        Assert.Equal(initiative.Id, progressEntry.InitiativeId);
        Assert.Equal("50%", progressEntry.NewValue);
    }

    /// <summary>
    /// A deactivated milestone must stop contributing to progress, weight totals and the milestone
    /// count, otherwise removing a completed milestone would keep inflating the weighted average.
    /// </summary>
    [Fact]
    public async Task DeleteMilestone_ExcludesTheDeactivatedMilestoneFromProgressAndWeights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);
        var dropped = await SeedMilestoneAsync(lease, initiative, 60m, 100);
        await SeedMilestoneAsync(lease, initiative, 40m, 0);

        var factory = BuildFactory();
        var repository = new InitiativeRepository(lease.Context);

        var before = factory.BuildProgress(await repository.GetDetailAsync(initiative.Id)!);
        Assert.Equal(60m, before.ProgressPercentage);
        Assert.Equal(100m, before.TotalWeight);

        var user = Owner(owner, Domain.Constants.Permissions.InitiativesManageMilestones);
        var handler = new DeleteMilestoneCommandHandler(
            repository,
            new MilestoneRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            BuildGuard(lease, user),
            factory);

        await handler.Handle(
            new DeleteMilestoneCommandRequest
            {
                InitiativeId = initiative.Id,
                Id = dropped.Id
            },
            CancellationToken.None);

        // The remaining milestone is the 0%/0-weight one, so progress must fall to zero even though
        // the deactivated 100%-complete milestone is still sitting in the table.
        var detail = await repository.GetDetailAsync(initiative.Id);
        Assert.NotNull(detail);
        Assert.Single(detail!.Milestones);

        var after = factory.BuildProgress(detail);
        Assert.Equal(0m, after.ProgressPercentage);
        Assert.Equal(40m, after.TotalWeight);

        // The grid aggregate must stop counting it too.
        var page = await repository.SearchAsync(
            new InitiativeListQuery { Page = 1, PageSize = 10, Paged = true },
            CancellationToken.None);

        var row = Assert.Single(page.Items);
        Assert.Equal(1, row.MilestoneCount);
        Assert.Equal(40m, row.TotalMilestoneWeight);
    }

    /// <summary>
    /// Delete deactivates rather than removes: the row and its whole graph stay in the database,
    /// the global query filter hides them from every read, and - because the row survives - the
    /// deletion can finally be audited in the very history it hides from. The previous hard-delete
    /// implementation had to skip that audit entry because the FK rejected the insert (error 547).
    /// </summary>
    [Fact]
    public async Task DeleteInitiative_DeactivatesTheRowKeepsHistoryAndAuditsTheDeletion()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.InProgress);
        await SeedMilestoneAsync(lease, initiative, 100m, 0);

        lease.Context.InitiativeUpdateLogs.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "MilestoneCreated",
            Details = "Milestone added."
        });
        await lease.Context.SaveChangesAsync();

        var handler = BuildDeleteInitiativeHandler(lease);

        var result = await handler.Handle(
            new DeleteInitiativeCommandRequest { Id = initiative.Id },
            CancellationToken.None);

        Assert.True(result.Success);

        // Hidden from every normal read, which is what removes it from the grid and the detail route.
        Assert.Empty(await lease.Context.Initiatives.ToListAsync());

        // Nothing was removed: the row and its graph are all still in the database.
        var storedInitiative = await lease.Context.Initiatives
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(initiative.Id, storedInitiative.Id);
        Assert.True(storedInitiative.IsDeleted);

        // Initiative shadows CommonEntity.Status with InitiativeStatus, so deactivating must leave
        // the execution state alone rather than writing 0 into the workflow column.
        Assert.Equal(InitiativeStatus.InProgress, storedInitiative.Status);

        Assert.Single(await lease.Context.Milestones.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(2, await lease.Context.InitiativeUpdateLogs.IgnoreQueryFilters().CountAsync());

        // Deactivating the parent deliberately does not flag its children. The filter is per entity,
        // and every milestone and dependency endpoint resolves the parent initiative first, so a
        // hidden initiative already makes its whole graph unreachable. Keeping one reversible flag on
        // the aggregate root is what lets a restore bring the milestones back untouched.

        // The deletion is now auditable, because the parent row still exists to reference.
        var audit = await lease.Context.InitiativeUpdateLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(l => l.ChangeType == "InitiativeDeleted");

        Assert.Equal(initiative.Id, audit.InitiativeId);
    }

    [Fact]
    public async Task DeleteInitiative_RefusesAClosedInitiative()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = await SeedUserAsync(lease, "Owner One");
        var initiative = await SeedInitiativeAsync(lease, owner, InitiativeStatus.Completed);
        initiative.IsClosed = true;
        initiative.ClosedAtUtc = DateTime.UtcNow;
        await lease.Context.SaveChangesAsync();

        var handler = BuildDeleteInitiativeHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new DeleteInitiativeCommandRequest { Id = initiative.Id },
            CancellationToken.None));
    }

    private static DeleteInitiativeCommandHandler BuildDeleteInitiativeHandler(TestDbContextLease lease)
        => new(
            new InitiativeRepository(lease.Context),
            new InitiativeUpdateLogRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
}
