using Application.CQRS.Scorecard.Handlers.Commands;
using Application.CQRS.Scorecard.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Application.Validation.Scorecard;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Scorecards;

public class CloneScorecardHandlerTests
{
    private sealed record Fixture(Guid SourceFiscalYearId, Guid TargetFiscalYearId, Guid SourceScorecardId, Guid SourceKpiId, Guid ParentKpiId, Guid ChildKpiId);

    private static async Task<Fixture> CreateFixture(TestDbContextLease lease)
    {
        var service = new FiscalCalendarService();
        var sourceYear = service.CreateFiscalYear(2025);
        var targetYear = service.CreateFiscalYear(2026);
        var sourcePeriods = service.GeneratePeriods(sourceYear);
        var targetPeriods = service.GeneratePeriods(targetYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = sourceYear.Id, Name = "ECX Corporate Strategy 2025", Description = "Original", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 60 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Market Share", Weight = 100, UnitMeasure = "%" };
        var parentKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Total Sales",
            Unit = "ETB",
            Weight = 70,
            OptimizationType = KpiOptimizationType.Maximize,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var childKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            ParentKpiId = parentKpi.Id,
            Name = "Sales North",
            Unit = "ETB",
            Weight = 30,
            OptimizationType = KpiOptimizationType.Maximize,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.AddRange(sourceYear, targetYear);
        lease.Context.FiscalPeriods.AddRange(sourcePeriods);
        lease.Context.FiscalPeriods.AddRange(targetPeriods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.AddRange(parentKpi, childKpi);

        var targets = new List<KpiTarget>();
        foreach (var kpi in new[] { parentKpi, childKpi })
        {
            foreach (var period in sourcePeriods)
            {
                targets.Add(new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, TargetValue = period.FiscalMonth * 10, Granularity = TargetGranularity.Monthly });
            }

            var yearEnd = sourcePeriods.Single(p => p.IsYearEnd);
            targets.Add(new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = yearEnd.Id, TargetValue = 120, Granularity = TargetGranularity.Annual });
        }

        lease.Context.KpiTargets.AddRange(targets);
        await lease.Context.SaveChangesAsync();

        return new Fixture(sourceYear.Id, targetYear.Id, scorecard.Id, parentKpi.Id, parentKpi.Id, childKpi.Id);
    }

    private static (CloneScorecardCommandHandler handler, TestSpmsJobScheduler scheduler) CreateHandler(TestDbContextLease lease)
    {
        return (new CloneScorecardCommandHandler(
            new ScorecardRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KraRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context)), new TestSpmsJobScheduler());
    }

    [Fact]
    public async Task CloneScorecard_CopiesHierarchyAndTargetsIntoNewFiscalYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var (handler, _) = CreateHandler(lease);

        var response = await handler.Handle(new CloneScorecardCommandRequest
        {
            SourceScorecardId = fixture.SourceScorecardId,
            TargetFiscalYearId = fixture.TargetFiscalYearId,
            NewName = "ECX Corporate Strategy 2026"
        }, CancellationToken.None);

        Assert.True(response.Success);
        var result = response.Data!;
        Assert.Equal(1, result.GoalCount);
        Assert.Equal(1, result.KraCount);
        Assert.Equal(2, result.KpiCount);
        Assert.Equal(26, result.TargetCount); // 2 KPIs x (12 monthly + 1 annual)

        var newScorecard = await lease.Context.Scorecards.FirstAsync(s => s.Id == result.TargetScorecardId);
        Assert.Equal(fixture.TargetFiscalYearId, newScorecard.FiscalYearId);
        Assert.Equal("ECX Corporate Strategy 2026", newScorecard.Name);
        Assert.Equal(ScorecardStatus.Active, newScorecard.ScorecardStatus);
        Assert.Equal("Original", newScorecard.Description);
        Assert.Equal(ScorecardScope.Corporate, newScorecard.Scope);

        // Hierarchy fully cloned with weights preserved.
        var newGoal = await lease.Context.Goals.SingleAsync(g => g.ScorecardId == newScorecard.Id);
        Assert.Equal(60, newGoal.Weight);

        var newKra = await lease.Context.Kras.SingleAsync(k => k.ScorecardId == newScorecard.Id && k.GoalId == newGoal.Id);
        Assert.Equal(100, newKra.Weight);
        Assert.Equal("%", newKra.UnitMeasure);

        var newKpis = await lease.Context.Kpis.Where(k => k.ScorecardId == newScorecard.Id).ToListAsync();
        Assert.Equal(2, newKpis.Count);

        // Targets point at the target fiscal year's periods, by the same month.
        foreach (var newKpi in newKpis)
        {
            var targets = await lease.Context.KpiTargets
                .Include(t => t.FiscalPeriod)
                .Where(t => t.KpiId == newKpi.Id)
                .ToListAsync();
            Assert.Equal(13, targets.Count);
            Assert.All(targets, t => Assert.Equal(fixture.TargetFiscalYearId, t.FiscalPeriod!.FiscalYearId));
        }

        // Source scorecard is untouched.
        Assert.Equal(0, await lease.Context.KpiTargets.CountAsync(t => t.KpiId == fixture.SourceKpiId && t.FiscalPeriod!.FiscalYearId == fixture.TargetFiscalYearId));
    }

    [Fact]
    public async Task CloneScorecard_DefaultName_InheritsSourceName()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var (handler, _) = CreateHandler(lease);

        var response = await handler.Handle(new CloneScorecardCommandRequest
        {
            SourceScorecardId = fixture.SourceScorecardId,
            TargetFiscalYearId = fixture.TargetFiscalYearId
        }, CancellationToken.None);

        Assert.Equal("ECX Corporate Strategy 2025", response.Data!.Name);
    }

    [Fact]
    public async Task CloneScorecard_RemapsParentKpiLink()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var (handler, _) = CreateHandler(lease);

        var response = await handler.Handle(new CloneScorecardCommandRequest
        {
            SourceScorecardId = fixture.SourceScorecardId,
            TargetFiscalYearId = fixture.TargetFiscalYearId
        }, CancellationToken.None);

        var newKpis = await lease.Context.Kpis.Where(k => k.ScorecardId == response.Data!.TargetScorecardId).ToListAsync();
        var newChild = newKpis.Single(k => k.Name == "Sales North");
        var newParent = newKpis.Single(k => k.Name == "Total Sales");

        Assert.Equal(newParent.Id, newChild.ParentKpiId);
        Assert.NotEqual(fixture.ParentKpiId, newChild.ParentKpiId);
    }

    [Fact]
    public async Task CloneScorecard_NameCollision_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var (handler, _) = CreateHandler(lease);

        lease.Context.Scorecards.Add(new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fixture.TargetFiscalYearId, Name = "ECX Corporate Strategy 2025", Scope = ScorecardScope.Corporate });
        await lease.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CloneScorecardCommandRequest
            {
                SourceScorecardId = fixture.SourceScorecardId,
                TargetFiscalYearId = fixture.TargetFiscalYearId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CloneScorecard_UnknownSource_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var (handler, _) = CreateHandler(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CloneScorecardCommandRequest
            {
                SourceScorecardId = Guid.NewGuid(),
                TargetFiscalYearId = fixture.TargetFiscalYearId
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CloneScorecard_UnknownTargetYear_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var (handler, _) = CreateHandler(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CloneScorecardCommandRequest
            {
                SourceScorecardId = fixture.SourceScorecardId,
                TargetFiscalYearId = Guid.NewGuid()
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CloneScorecard_TargetYearWithoutPeriods_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);

        var emptyYear = new FiscalCalendarService().CreateFiscalYear(2027);
        lease.Context.FiscalYears.Add(emptyYear);
        await lease.Context.SaveChangesAsync();

        var (handler, _) = CreateHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CloneScorecardCommandRequest
            {
                SourceScorecardId = fixture.SourceScorecardId,
                TargetFiscalYearId = emptyYear.Id
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CloneScorecard_PreservesNames_AndKeepsDistinctSiblings()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var calendar = new FiscalCalendarService();
        var sourceYear = calendar.CreateFiscalYear(2025);
        var targetYear = calendar.CreateFiscalYear(2026);
        var sourcePeriods = calendar.GeneratePeriods(sourceYear);
        var targetPeriods = calendar.GeneratePeriods(targetYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = sourceYear.Id, Name = "Punct Strategy", Scope = ScorecardScope.Corporate };

        // Under the old normalization both names collapsed to "Grow Revenue", so the second
        // goal was silently dropped. They are distinct rows and must both survive the clone
        // with their original punctuation intact.
        var goal1 = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 50 };
        var goal2 = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue!", Weight = 50 };

        lease.Context.FiscalYears.AddRange(sourceYear, targetYear);
        lease.Context.FiscalPeriods.AddRange(sourcePeriods);
        lease.Context.FiscalPeriods.AddRange(targetPeriods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.AddRange(goal1, goal2);
        await lease.Context.SaveChangesAsync();

        var (handler, _) = CreateHandler(lease);
        var response = await handler.Handle(new CloneScorecardCommandRequest
        {
            SourceScorecardId = scorecard.Id,
            TargetFiscalYearId = targetYear.Id,
            NewName = "Punct Strategy 2026"
        }, CancellationToken.None);

        Assert.Equal(2, response.Data!.GoalCount);
        var clonedNames = await lease.Context.Goals
            .Where(g => g.ScorecardId == response.Data.TargetScorecardId)
            .Select(g => g.Name)
            .ToListAsync();
        Assert.Contains("Grow Revenue", clonedNames);
        Assert.Contains("Grow Revenue!", clonedNames);
    }

    [Fact]
    public void CloneScorecardValidator_RequiresSourceAndTarget()
    {
        var validator = new CloneScorecardValidator();
        var result = validator.Validate(new CloneScorecardCommandRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Source scorecard", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Target fiscal year", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CloneScorecardValidator_LimitsNewNameLength()
    {
        var validator = new CloneScorecardValidator();
        var result = validator.Validate(new CloneScorecardCommandRequest
        {
            SourceScorecardId = Guid.NewGuid(),
            TargetFiscalYearId = Guid.NewGuid(),
            NewName = new string('a', 201)
        });

        Assert.False(result.IsValid);
    }
}
