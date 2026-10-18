using Application.CQRS.Goals.Handlers.Commands;
using Application.CQRS.Goals.Requests.Commands;
using Application.DTOS.Goal;
using Application.Exceptions;
using Application.Interface;
using Application.Mapping;
using Application.Services;
using Application.Tests.TestSupport;
using Application.Validation.Goal;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;
using FluentValidation.TestHelper;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Goals;

public class GoalWeightBudgetTests
{
    private static IMapper Mapper() => new MapperConfiguration(
        configuration => configuration.AddProfile<MappingProfile>(),
        NullLoggerFactory.Instance).CreateMapper();

    private static CreateGoalCommandHandler CreateHandler(TestDbContextLease lease)
        => new(
            new GoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            Mapper());

    private static UpdateGoalCommandHandler UpdateHandler(TestDbContextLease lease)
        => new(
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

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

    private static CreateGoalCommandRequest CreateRequest(Guid scorecardId, string name, decimal weight)
        => new()
        {
            Dto = new CreateGoalDto
            {
                ScorecardId = scorecardId,
                Name = name,
                Weight = weight
            }
        };

    // ── FRD boundary: a goal weight must be > 0 and <= 100 ──────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    public async Task Validators_RejectWeightsOutsideRange(decimal weight)
    {
        var createValidator = new CreateGoalValidator();
        var updateValidator = new UpdateGoalValidator();

        Assert.NotEmpty(createValidator.Validate(new CreateGoalCommandRequest
        {
            Dto = new CreateGoalDto { ScorecardId = Guid.NewGuid(), Name = "G", Weight = weight }
        }).Errors);

        Assert.NotEmpty(updateValidator.Validate(new UpdateGoalCommandRequest { Id = Guid.NewGuid(), Weight = weight }).Errors);
    }

    [Fact]
    public void Validators_AcceptWeightInRange()
    {
        var createValidator = new CreateGoalValidator();
        var updateValidator = new UpdateGoalValidator();
        var scorecardId = Guid.NewGuid();

        Assert.Empty(createValidator.Validate(new CreateGoalCommandRequest
        {
            Dto = new CreateGoalDto { ScorecardId = scorecardId, Name = "G", Weight = 100m }
        }).Errors);

        Assert.Empty(createValidator.Validate(new CreateGoalCommandRequest
        {
            Dto = new CreateGoalDto { ScorecardId = scorecardId, Name = "G", Weight = 0.5m }
        }).Errors);

        Assert.Empty(updateValidator.Validate(new UpdateGoalCommandRequest { Id = Guid.NewGuid(), Weight = 100m }).Errors);

        Assert.Empty(updateValidator.Validate(new UpdateGoalCommandRequest { Id = Guid.NewGuid(), Name = "name only" }).Errors);
    }

    [Fact]
    public async Task Database_CheckConstraint_RejectsZeroOrAbove100Weights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);

        lease.Context.Goals.Add(new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            Name = "Zero weight",
            Weight = 0m
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());

        lease.Context.Entry(lease.Context.Goals.Local.Single()).State = EntityState.Detached;
        lease.Context.Goals.Add(new Goal
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            Name = "Over weight",
            Weight = 120m
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());
    }

    // ── Scorecard budget: sum must never exceed 100, below is allowed ───────

    [Fact]
    public async Task Create_Exactly100Total_IsAllowed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);

        await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "First", 40m), CancellationToken.None);
        await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Second", 60m), CancellationToken.None);

        var weights = await lease.Context.Goals.AsNoTracking()
            .Where(g => g.ScorecardId == scorecard.Id)
            .Select(g => g.Weight)
            .ToListAsync();
        Assert.Equal(100m, weights.Sum());
    }

    [Fact]
    public async Task Create_TotalBelow100_IsAllowed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);

        var response = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "First", 40m), CancellationToken.None);

        Assert.True(response.Success);
        var saved = await lease.Context.Goals.AsNoTracking().SingleAsync();
        Assert.Equal(40m, saved.Weight);
        Assert.Equal(40m, response.Data!.Weight);
    }

    [Fact]
    public async Task Create_GoalPushingTotalAbove100_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);

        await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "First", 60m), CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Second", 41m), CancellationToken.None));

        Assert.Contains("exceeds 100%", exception.Message);
        Assert.Equal(1, await lease.Context.Goals.CountAsync());
    }

    [Fact]
    public async Task Create_MissingScorecard_IsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(Guid.NewGuid(), "Orphan", 50m), CancellationToken.None));

        Assert.IsType<NotFoundException>(exception);
    }

    [Fact]
    public async Task Create_DuplicateNameInScorecard_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);

        await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Grow Revenue", 40m), CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "GROW REVENUE", 10m), CancellationToken.None));

        Assert.Contains("already exists", exception.Message);
        Assert.Equal(1, await lease.Context.Goals.CountAsync());
    }

    // ── Update budget behavior ───────────────────────────────────────────────

    [Fact]
    public async Task Update_WeightChangeExceedingBudget_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var createResponse = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Grow Revenue", 60m), CancellationToken.None);
        await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Second", 40m), CancellationToken.None);
        var goalId = createResponse.Data!.Id;

        var exception = await Assert.ThrowsAnyAsync<ApplicationException>(
            () => UpdateHandler(lease).Handle(new UpdateGoalCommandRequest { Id = goalId, Weight = 70m }, CancellationToken.None));

        // Others hold 40; the edit would put the scorecard at 110.
        Assert.Contains("exceeds 100%", exception.Message);
        var saved = await lease.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goalId);
        Assert.Equal(60m, saved.Weight);
    }

    [Fact]
    public async Task Update_WeightChangeToExactly100_IsAllowed()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var createResponse = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Grow Revenue", 60m), CancellationToken.None);
        var goalId = createResponse.Data!.Id;

        await UpdateHandler(lease).Handle(new UpdateGoalCommandRequest { Id = goalId, Weight = 100m }, CancellationToken.None);

        var saved = await lease.Context.Goals.AsNoTracking().SingleAsync();
        Assert.Equal(100m, saved.Weight);
    }

    [Fact]
    public async Task Update_DoesNotOverwriteOtherGoalsWeights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var first = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "First", 40m), CancellationToken.None);
        var second = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Second", 30m), CancellationToken.None);

        await UpdateHandler(lease).Handle(new UpdateGoalCommandRequest { Id = first.Data!.Id, Weight = 50m }, CancellationToken.None);

        var weights = await lease.Context.Goals.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Weight);
        Assert.Equal(50m, weights[first.Data.Id]);
        Assert.Equal(30m, weights[second.Data!.Id]);
    }

    // ── Transaction semantics (ADR-009 concurrency seam) ────────────────────

    [Fact]
    public async Task UnitOfWork_Transaction_RollsBackWhenWorkThrows()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var unitOfWork = new SpmsUnitOfWork(lease.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync<bool>(async () =>
        {
            lease.Context.Goals.Add(new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Rolled back", Weight = 10m });
            await unitOfWork.SaveChangesAsync();
            throw new InvalidOperationException("simulated validation failure");
        }));

        Assert.Equal(0, await lease.Context.Goals.CountAsync());
    }

    [Fact]
    public async Task UnitOfWork_Transaction_PersistsOnCommit()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var unitOfWork = new SpmsUnitOfWork(lease.Context);

        var result = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            lease.Context.Goals.Add(new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Committed", Weight = 10m });
            await unitOfWork.SaveChangesAsync();
            return "done";
        });

        Assert.Equal("done", result);
        Assert.Equal(1, await lease.Context.Goals.CountAsync());
    }

    [Fact]
    public async Task SqlCommand_SumsActiveGoalWeights()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scorecard = await SeedScorecardAsync(lease);
        var first = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "First", 40m), CancellationToken.None);
        var second = await CreateHandler(lease).Handle(CreateRequest(scorecard.Id, "Second", 30m), CancellationToken.None);
        var repository = new GoalRepository(lease.Context);

        var total = await repository.GetWeightsSumAsync(scorecard.Id);
        var excludingFirst = await repository.GetWeightsSumAsync(scorecard.Id, first.Data!.Id);
        var excludingSecond = await repository.GetWeightsSumAsync(scorecard.Id, second.Data!.Id);

        Assert.Equal(70m, total);
        Assert.Equal(30m, excludingFirst);
        Assert.Equal(40m, excludingSecond);
        Assert.Equal(0m, await repository.GetWeightsSumAsync(Guid.NewGuid()));
    }

    // Concurrent creates racing on the same scorecard cannot be reproduced over one
    // in-memory SQLite connection; on SQL Server the protection above (serializable
    // transaction wrapping the SQL sum + insert) serializes the writers per scorecard.
}
