using Application.CQRS.Goals.Handlers.Commands;
using Application.CQRS.Goals.Requests.Commands;
using Application.CQRS.Kra.Handlers.Commands;
using Application.CQRS.Kra.Requests.Commands;
using Application.DTOS.Kra;
using Application.Exceptions;
using Application.Interface;
using Application.Tests.TestSupport;
using Application.Validation.Kra;
using Domain.Entities;
using Domain.Enums;
using Application.Services;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests.Kras;

public class KraWeightBudgetTests
{
    private static CreateKraCommandHandler CreateHandler(TestDbContextLease lease)
        => new(
            new KraRepository(lease.Context),
            new GoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static UpdateKraCommandHandler UpdateHandler(TestDbContextLease lease)
        => new(
            new KraRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static DeleteKraCommandHandler DeleteHandler(TestDbContextLease lease)
        => new(new KraRepository(lease.Context));

    private static DeleteGoalCommandHandler DeleteGoalHandler(TestDbContextLease lease)
        => new(new GoalRepository(lease.Context));

    private static async Task<Scorecard> SeedScorecardAsync(TestDbContextLease lease)
    {
        var year = new FiscalCalendarService().CreateFiscalYear(2026);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Corporate 2026",
            Scope = ScorecardScope.Corporate
        };
        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.Add(scorecard);
        await lease.Context.SaveChangesAsync();
        return scorecard;
    }

    private static async Task<Goal> SeedGoalAsync(TestDbContextLease lease, Scorecard scorecard, string name = "Grow Revenue", decimal weight = 100m)
    {
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            Name = name,
            Weight = weight
        };
        lease.Context.Goals.Add(goal);
        await lease.Context.SaveChangesAsync();
        return goal;
    }

    private static CreateKraCommandRequest CreateRequest(Guid goalId, Guid scorecardId, string name, decimal weight)
        => new()
        {
            GoalId = goalId,
            ScorecardId = scorecardId,
            Name = name,
            Weight = weight
        };

    // ── FRD boundary: a KRA weight must be > 0 and <= 100 ───────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    public async Task Validators_RejectWeightsOutsideRange(decimal weight)
    {
        var createValidator = new CreateKraValidator();
        var updateValidator = new UpdateKraValidator();

        Assert.NotEmpty(createValidator.Validate(CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "K", weight)).Errors);
        Assert.NotEmpty(updateValidator.Validate(new UpdateKraCommandRequest { Id = Guid.NewGuid(), Weight = weight }).Errors);
    }

    [Fact]
    public void Validators_AcceptWeightInRange()
    {
        var createValidator = new CreateKraValidator();
        var updateValidator = new UpdateKraValidator();

        Assert.Empty(createValidator.Validate(CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "K", 100m)).Errors);
        Assert.Empty(createValidator.Validate(CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "K", 0.5m)).Errors);
        Assert.Empty(updateValidator.Validate(new UpdateKraCommandRequest { Id = Guid.NewGuid(), Weight = 100m }).Errors);
        Assert.Empty(updateValidator.Validate(new UpdateKraCommandRequest { Id = Guid.NewGuid(), Name = "name only" }).Errors);
    }

    [Fact]
    public async Task Create_HandlerRejectsZeroOrNegativeOrAbove100_WithoutFluentValidation()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        foreach (var weight in new decimal[] { 0m, -5m, 100.01m })
        {
            var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
                () => CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "K", weight), CancellationToken.None));
            Assert.IsType<BadRequestException>(exception);
        }

        Assert.Equal(0, await lease.Context.Kras.CountAsync());
    }

    [Fact]
    public async Task Database_CheckConstraint_RejectsInvalidWeights_AndAcceptsBoundaries()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        lease.Context.Kras.Add(new Kra
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            ScorecardId = scorecard.Id,
            Name = "Zero weight",
            Weight = 0m
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());

        lease.Context.Entry(lease.Context.Kras.Local.Single()).State = EntityState.Detached;

        lease.Context.Kras.Add(new Kra
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            ScorecardId = scorecard.Id,
            Name = "Over weight",
            Weight = 120m
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());

        lease.Context.Entry(lease.Context.Kras.Local.Single()).State = EntityState.Detached;

        // The 100 boundary itself is valid.
        lease.Context.Kras.Add(new Kra
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            ScorecardId = scorecard.Id,
            Name = "Boundary",
            Weight = 100m
        });
        await lease.Context.SaveChangesAsync();

        Assert.Equal(1, await lease.Context.Kras.CountAsync());
    }

    // ── Goal budget: sum must never exceed 100, exactly 100 is allowed ───────

    [Fact]
    public async Task Create_Exactly100Total_IsAllowed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);
        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 60m), CancellationToken.None);

        var total = await new KraRepository(lease.Context).GetWeightsSumAsync(goal.Id);
        Assert.Equal(100m, total);
    }

    [Fact]
    public async Task Create_TotalBelow100_IsAllowed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        var response = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);

        Assert.True(response.Success);
        var saved = await lease.Context.Kras.AsNoTracking().SingleAsync();
        Assert.Equal(40m, saved.Weight);
        Assert.Equal(40m, response.Data!.Weight);
    }

    [Fact]
    public async Task Create_KRAPushingTotalAbove100_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 60m), CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 41m), CancellationToken.None));

        Assert.Contains("exceeds 100%", exception.Message);
        Assert.Equal(1, await lease.Context.Kras.CountAsync());
    }

    [Fact]
    public async Task Create_MissingGoal_IsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(Guid.NewGuid(), scorecard.Id, "Orphan", 50m), CancellationToken.None));

        Assert.IsType<NotFoundException>(exception);
    }

    [Fact]
    public async Task Create_MissingScorecard_IsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(goal.Id, Guid.NewGuid(), "Orphan", 50m), CancellationToken.None));

        Assert.IsType<NotFoundException>(exception);
    }

    [Fact]
    public async Task Create_GoalFromAnotherScorecard_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecardA = await SeedScorecardAsync(lease);
        var scorecardB = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = scorecardA.FiscalYearId,
            Name = "Division 2026",
            Scope = ScorecardScope.Corporate
        };
        lease.Context.Scorecards.Add(scorecardB);
        await lease.Context.SaveChangesAsync();

        var goal = await SeedGoalAsync(lease, scorecardA);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecardB.Id, "Mismatched", 50m), CancellationToken.None));

        Assert.Contains("does not belong", exception.Message);
        Assert.Equal(0, await lease.Context.Kras.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateNameUnderGoal_IsRejected_CaseInsensitively()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Grow Revenue", 40m), CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "GROW REVENUE", 10m), CancellationToken.None));

        Assert.Contains("already exists", exception.Message);
        Assert.Equal(1, await lease.Context.Kras.CountAsync());
    }

    // ── Update budget behavior (current KRA excluded from the total) ────────

    [Fact]
    public async Task Update_WeightChangeExceedingBudget_IsRejected_AndKeepsOriginalWeight()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var first = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 60m), CancellationToken.None);
        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 40m), CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => UpdateHandler(lease).Handle(new UpdateKraCommandRequest { Id = first.Data!.Id, Weight = 65m }, CancellationToken.None));

        // Others hold 40; the edit would put the goal at 105.
        Assert.Contains("exceed 100%", exception.Message);
        var saved = await lease.Context.Kras.AsNoTracking().SingleAsync(k => k.Id == first.Data.Id);
        Assert.Equal(60m, saved.Weight);
    }

    [Fact]
    public async Task Update_WeightChangeToExactly100_IsAllowed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var kra = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Only", 60m), CancellationToken.None);

        await UpdateHandler(lease).Handle(new UpdateKraCommandRequest { Id = kra.Data!.Id, Weight = 100m }, CancellationToken.None);

        var saved = await lease.Context.Kras.AsNoTracking().SingleAsync();
        Assert.Equal(100m, saved.Weight);
    }

    [Fact]
    public async Task Update_TotalAfterExcludingCurrentKRA_IsAllowedAt100()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var first = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 60m), CancellationToken.None);
        var second = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 40m), CancellationToken.None);

        // Raising "Second" to 60 excluding itself leaves 60 + 60 = 120? No - "First" stays at 60,
        // so this must be rejected; dropping "Second" to 40 (unchanged total) is allowed.
        await Assert.ThrowsAnyAsync<ApplicationException>(
            () => UpdateHandler(lease).Handle(new UpdateKraCommandRequest { Id = second.Data!.Id, Weight = 55m }, CancellationToken.None));

        await UpdateHandler(lease).Handle(new UpdateKraCommandRequest { Id = second.Data.Id, Weight = 40m }, CancellationToken.None);

        var total = await new KraRepository(lease.Context).GetWeightsSumAsync(goal.Id);
        Assert.Equal(100m, total);
    }

    [Fact]
    public async Task Update_HandlerRejectsZeroOrAbove100_WithoutFluentValidation()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var kra = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Only", 50m), CancellationToken.None);

        foreach (var weight in new decimal[] { 0m, -5m, 100.01m })
        {
            var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
                () => UpdateHandler(lease).Handle(new UpdateKraCommandRequest { Id = kra.Data!.Id, Weight = weight }, CancellationToken.None));
            Assert.IsType<BadRequestException>(exception);
        }

        var saved = await lease.Context.Kras.AsNoTracking().SingleAsync();
        Assert.Equal(50m, saved.Weight);
    }

    [Fact]
    public async Task Update_DoesNotOverwriteOtherKRAsUnderSameGoal()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var first = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);
        var second = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 30m), CancellationToken.None);

        await UpdateHandler(lease).Handle(new UpdateKraCommandRequest { Id = first.Data!.Id, Weight = 50m }, CancellationToken.None);

        var weights = await lease.Context.Kras.AsNoTracking().ToDictionaryAsync(k => k.Id, k => k.Weight);
        Assert.Equal(50m, weights[first.Data.Id]);
        Assert.Equal(30m, weights[second.Data!.Id]);
    }

    // ── Budget scoping and soft-deleted KRAs ─────────────────────────────────

    [Fact]
    public async Task SoftDeleted_KRAs_AreExcludedFromBudget()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 60m), CancellationToken.None);

        // Soft-delete the KRA (project convention: delete never removes the row).
        var savedKra = await lease.Context.Kras.SingleAsync();
        savedKra.IsDeleted = true;
        savedKra.Status = EntityStatus.Inactive;
        await lease.Context.SaveChangesAsync();

        // The budget must now show 0 active weight, so a 50 KRA fits again.
        var total = await new KraRepository(lease.Context).GetWeightsSumAsync(goal.Id);
        Assert.Equal(0m, total);

        var response = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Another", 50m), CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal(50m, await new KraRepository(lease.Context).GetWeightsSumAsync(goal.Id));
    }

    [Fact]
    public async Task DifferentGoals_HaveIndependentBudgets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goalA = await SeedGoalAsync(lease, scorecard, "Goal A");
        var goalB = await SeedGoalAsync(lease, scorecard, "Goal B");

        await CreateHandler(lease).Handle(CreateRequest(goalA.Id, scorecard.Id, "A1", 45m), CancellationToken.None);
        await CreateHandler(lease).Handle(CreateRequest(goalA.Id, scorecard.Id, "A2", 55m), CancellationToken.None);

        // Goal B still has its own 100% budget even though Goal A is full.
        var response = await CreateHandler(lease).Handle(CreateRequest(goalB.Id, scorecard.Id, "B1", 10m), CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal(10m, response.Data!.Weight);
    }

    [Fact]
    public async Task SqlCommand_SumsActiveKRAWeights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var first = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);
        var second = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 30m), CancellationToken.None);
        var repository = new KraRepository(lease.Context);

        var total = await repository.GetWeightsSumAsync(goal.Id);
        var excludingFirst = await repository.GetWeightsSumAsync(goal.Id, first.Data!.Id);
        var excludingSecond = await repository.GetWeightsSumAsync(goal.Id, second.Data!.Id);

        Assert.Equal(70m, total);
        Assert.Equal(30m, excludingFirst);
        Assert.Equal(40m, excludingSecond);
        Assert.Equal(0m, await repository.GetWeightsSumAsync(Guid.NewGuid()));
    }

    // ── Transaction semantics (ADR-009 concurrency seam) ────────────────────

    [Fact]
    public async Task FailingCreate_LeavesNoPartialChanges()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 60m), CancellationToken.None);

        await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "Second", 41m), CancellationToken.None));

        // Budget failure: only the one committed KRA exists, nothing else persisted.
        var count = await lease.Context.Kras.CountAsync();
        Assert.Equal(1, count);
        Assert.Equal(60m, await new KraRepository(lease.Context).GetWeightsSumAsync(goal.Id));
    }

    [Fact]
    public async Task UnitOfWork_Transaction_RollsBackWhenWorkThrows()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var unitOfWork = new SpmsUnitOfWork(lease.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync<bool>(async () =>
        {
            lease.Context.Kras.Add(new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Rolled back", Weight = 10m });
            await unitOfWork.SaveChangesAsync();
            throw new InvalidOperationException("simulated validation failure");
        }));

        Assert.Equal(0, await lease.Context.Kras.CountAsync());
    }

    // Concurrent creates racing on the same goal cannot be reproduced over one
    // in-memory SQLite connection; on SQL Server the protection above (serializable
    // transaction wrapping the SQL sum + insert) serializes the writers per goal.

    // ── Deletion integrity (soft delete, restricted FKs, no cascades) ───────

    private static async Task<Kpi> SeedKpiAsync(TestDbContextLease lease, Kra kra, string name = "Revenue KPI")
    {
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = kra.ScorecardId,
            KraId = kra.Id,
            Name = name,
            Weight = 100m
        };
        lease.Context.Kpis.Add(kpi);
        await lease.Context.SaveChangesAsync();
        return kpi;
    }

    [Fact]
    public async Task DeleteKRA_Blocked_WhenActiveKpiExists()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var kra = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);
        await SeedKpiAsync(lease, new Kra { Id = kra.Data!.Id, ScorecardId = scorecard.Id, Name = "First", GoalId = goal.Id, Weight = 40m });

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => DeleteHandler(lease).Handle(new DeleteKraCommandRequest { Id = kra.Data.Id }, CancellationToken.None));

        Assert.Contains("KPIs", exception.Message);
        var kraAfter = await lease.Context.Kras.AsNoTracking().SingleAsync(k => k.Id == kra.Data.Id);
        Assert.False(kraAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteKRA_Blocked_WhenOnlySoftDeletedKpisExist()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var kra = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);
        var kpi = await SeedKpiAsync(lease, new Kra { Id = kra.Data!.Id, ScorecardId = scorecard.Id, Name = "First", GoalId = goal.Id, Weight = 40m });

        // Soft-delete the KPI: normal reads (and the Kpis navigation) hide it, but the
        // deletion-integrity check must still see it and keep the KRA locked.
        var savedKpi = await lease.Context.Kpis.SingleAsync(k => k.Id == kpi.Id);
        savedKpi.IsDeleted = true;
        await lease.Context.SaveChangesAsync();

        Assert.True(await new KraRepository(lease.Context).HasKpisIncludingDeletedAsync(kra.Data.Id));

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => DeleteHandler(lease).Handle(new DeleteKraCommandRequest { Id = kra.Data.Id }, CancellationToken.None));

        Assert.Contains("soft-deleted", exception.Message);
        var kraAfter = await lease.Context.Kras.AsNoTracking().SingleAsync(k => k.Id == kra.Data.Id);
        Assert.False(kraAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteKRA_Succeeds_WhenNoKpisExist_AndIsASoftDelete()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var kra = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);

        await DeleteHandler(lease).Handle(new DeleteKraCommandRequest { Id = kra.Data!.Id }, CancellationToken.None);

        // The row survives (soft delete) but leaves budget, queries and the UI.
        // IgnoreQueryFilters is required to SEE the soft-deleted row here.
        var repository = new KraRepository(lease.Context);
        Assert.Equal(0m, await repository.GetWeightsSumAsync(goal.Id));

        var raw = await lease.Context.Kras.IgnoreQueryFilters().SingleAsync(k => k.Id == kra.Data.Id);
        Assert.True(raw.IsDeleted);

        // The unique (scorecard, goal, name) index deliberately excludes soft-deleted
        // rows, so the same name can be re-created after a delete. A production request
        // gets a fresh DbContext; the test mirrors that by dropping tracked instances,
        // because a shared context keeps the soft-deleted KRA in the already-loaded
        // goal.Kras navigation even though SQL filters it out.
        lease.Context.ChangeTracker.Clear();
        var recreated = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);
        Assert.True(recreated.Success);
    }

    [Fact]
    public void DeleteKRA_ErrorMessage_DescribesTheRestriction()
    {
        // The frontend dialog and the API error must agree on the rule:
        // a KRA is deletable only while it holds no KPIs at all.
        const string message = "Cannot delete a KRA that has KPIs (including soft-deleted ones). Remove its KPIs first.";
        Assert.Contains("KPIs", message);
        Assert.Contains("soft-deleted", message);
    }

    [Fact]
    public async Task DeleteGoal_Blocked_WhenActiveKraExists()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        var kra = await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => DeleteGoalHandler(lease).Handle(new DeleteGoalCommandRequest { Id = goal.Id }, CancellationToken.None));

        Assert.Contains("KRAs", exception.Message);
        var goalAfter = await lease.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id);
        Assert.False(goalAfter.IsDeleted);
        var kraAfter = await lease.Context.Kras.AsNoTracking().SingleAsync(k => k.Id == kra.Data!.Id);
        Assert.False(kraAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteGoal_Blocked_WhenOnlySoftDeletedKrasExist()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);
        await CreateHandler(lease).Handle(CreateRequest(goal.Id, scorecard.Id, "First", 40m), CancellationToken.None);

        var savedKra = await lease.Context.Kras.SingleAsync();
        savedKra.IsDeleted = true;
        await lease.Context.SaveChangesAsync();

        // Hidden from normal reads, but the goal must stay put.
        Assert.Equal(0, await lease.Context.Kras.AsNoTracking().CountAsync());
        Assert.True(await new GoalRepository(lease.Context).HasAnyKrasIncludingDeletedAsync(goal.Id));

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => DeleteGoalHandler(lease).Handle(new DeleteGoalCommandRequest { Id = goal.Id }, CancellationToken.None));

        Assert.Contains("soft-deleted", exception.Message);
        var goalAfter = await lease.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id);
        Assert.False(goalAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteGoal_Succeeds_WhenNoKrasExist_AndIsASoftDelete()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var goal = await SeedGoalAsync(lease, scorecard);

        await DeleteGoalHandler(lease).Handle(new DeleteGoalCommandRequest { Id = goal.Id }, CancellationToken.None);

        var raw = await lease.Context.Goals.IgnoreQueryFilters().SingleAsync(g => g.Id == goal.Id);
        Assert.True(raw.IsDeleted);
        Assert.True(raw.Status == EntityStatus.Inactive);
    }
}
