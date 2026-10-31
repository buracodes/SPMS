using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Persistence;

public class SoftDeleteQueryFilterTests
{
    [Fact]
    public async Task CommonEntityQueries_ExcludeSoftDeletedRows()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Test Scorecard",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        await lease.Context.SaveChangesAsync();

        lease.Context.Goals.AddRange(
            new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Visible goal", Weight = 1 },
            new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Deleted goal", IsDeleted = true, Weight = 1 });
        await lease.Context.SaveChangesAsync();

        var visibleGoals = await lease.Context.Goals.AsNoTracking().ToListAsync();
        var allGoals = await lease.Context.Goals.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        Assert.Single(visibleGoals);
        Assert.Equal("Visible goal", visibleGoals[0].Name);
        Assert.Equal(2, allGoals.Count);
    }

    /// <summary>
    /// Delete must never remove a row. It deactivates the record by setting IsDeleted and, where the
    /// entity actually has the generic lifecycle flag, moving Status from Active (1) to Inactive (0).
    /// </summary>
    [Fact]
    public async Task Delete_FlipsTheLifecycleStatusFromActiveToInactiveWithoutRemovingTheRow()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Test Scorecard",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Doomed goal", Weight = 1 };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        await lease.Context.SaveChangesAsync();

        Assert.Equal(EntityStatus.Active, goal.Status);

        await new GenericRepository<Goal>(lease.Context).Delete(goal);

        // Still physically present, with the lifecycle flag flipped 1 -> 0.
        var stored = await lease.Context.Goals.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal(goal.Id, stored.Id);
        Assert.True(stored.IsDeleted);
        Assert.Equal(EntityStatus.Inactive, stored.Status);

        // And therefore gone from every ordinary read.
        Assert.Empty(await lease.Context.Goals.ToListAsync());
    }

    /// <summary>
    /// A status of Inactive on its own is enough to hide a record, so legacy rows that were
    /// deactivated by flipping Status without touching IsDeleted still disappear from the UI.
    /// </summary>
    [Fact]
    public async Task InactiveStatusAlone_HidesTheRecordWithoutTheIsDeletedFlag()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Test Scorecard",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.AddRange(
            new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Visible goal", Weight = 1 },
            new Goal
            {
                Id = Guid.NewGuid(),
                ScorecardId = scorecard.Id,
                Name = "Inactive goal",
                Weight = 1,
                Status = EntityStatus.Inactive
            });
        await lease.Context.SaveChangesAsync();

        var visible = await lease.Context.Goals.AsNoTracking().ToListAsync();
        Assert.Single(visible);
        Assert.Equal("Visible goal", visible[0].Name);

        // Both rows are still in the table.
        Assert.Equal(2, await lease.Context.Goals.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>
    /// Initiative shadows CommonEntity.Status with InitiativeStatus, where 0 is not a valid value, so
    /// deactivating one must leave its execution status untouched instead of writing 0 into it.
    /// </summary>
    [Fact]
    public async Task Delete_LeavesShadowedExecutionStatusAloneAndOnlySetsIsDeleted()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var owner = new User
        {
            Id = Guid.NewGuid(),
            Username = "shadow.owner",
            Email = "shadow.owner@spms.local",
            FullName = "Shadow Owner",
            IsActive = true
        };
        var initiative = new Initiative
        {
            Id = Guid.NewGuid(),
            Name = "Shadowed status initiative",
            OwnerId = owner.Id,
            Status = InitiativeStatus.InProgress,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31)
        };

        lease.Context.Users.Add(owner);
        lease.Context.Initiatives.Add(initiative);
        await lease.Context.SaveChangesAsync();

        await new GenericRepository<Initiative>(lease.Context).Delete(initiative);

        var stored = await lease.Context.Initiatives.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.Equal(InitiativeStatus.InProgress, stored.Status);
        Assert.Empty(await lease.Context.Initiatives.ToListAsync());
    }
}