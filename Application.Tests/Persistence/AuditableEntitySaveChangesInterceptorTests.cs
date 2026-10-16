using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Persistence;

public class AuditableEntitySaveChangesInterceptorTests
{
    [Fact]
    public async Task SaveChangesAsync_CreatingCommonEntity_StampsEntityAndCreatesAuditLog()
    {
        var userId = Guid.NewGuid();
        var currentUser = new TestCurrentUserService
        {
            UserId = userId,
            UserName = "admin"
        };

        await using var lease = await TestDbContextFactory.CreateAsync(currentUser);
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

        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Improve service delivery", Weight = 1 };

        lease.Context.Goals.Add(goal);
        await lease.Context.SaveChangesAsync();

        var auditLog = await lease.Context.AuditLogEntries
            .SingleAsync(a => a.EntityType == nameof(Goal));

        Assert.Equal(userId, goal.CreatedBy);
        Assert.NotEqual(default, goal.CreatedDate);
        Assert.Equal(nameof(Goal), auditLog.EntityType);
        Assert.Equal(goal.Id.ToString(), auditLog.EntityId);
        Assert.Equal(AuditAction.Create, auditLog.Action);
        Assert.Equal(userId, auditLog.PerformedBy);
        Assert.Equal("admin", auditLog.PerformedByName);
        Assert.Null(auditLog.OldValues);
        Assert.Contains("improve service delivery", auditLog.NewValues!, StringComparison.OrdinalIgnoreCase);
    }
}