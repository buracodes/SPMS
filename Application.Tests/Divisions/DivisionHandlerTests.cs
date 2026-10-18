using Application.CQRS.Division.Handlers.Commands;
using Application.CQRS.Division.Handlers.Queries;
using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Service;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Divisions;

public class DivisionHandlerTests
{
    private sealed record Fixture(Guid FiscalYearId, Guid ScorecardId, Guid GoalAId, Guid GoalBId, Guid PeriodId);

    private static async Task<Fixture> CreateFixture(TestDbContextLease lease)
    {
        var service = new FiscalCalendarService();
        var year = service.CreateFiscalYear(2026);
        var periods = service.GeneratePeriods(year);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "ECX Corporate Strategy 2026", Scope = ScorecardScope.Corporate };
        var goalA = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 50 };
        var goalB = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Customer Satisfaction", Weight = 50 };
        var division = new Domain.Entities.Division { Id = divisionId, Name = "Retail Operations", Code = "RET" };

        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.AddRange(goalA, goalB);
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();

        return new Fixture(year.Id, scorecard.Id, goalA.Id, goalB.Id, periods[0].Id);
    }

    private static CreateDivisionCommandHandler CreateDivisionHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new UserRepository(lease.Context),
            new GenericRepository<Role>(lease.Context),
            new PasswordHasherService(),
            new SpmsUnitOfWork(lease.Context));

    private static async Task SeedDataInputRoleAsync(TestDbContextLease lease)
    {
        lease.Context.Roles.Add(new Role
        {
            Id = Guid.NewGuid(),
            Name = Roles.DataInputUser,
            Description = "Default Data Input User role."
        });
        await lease.Context.SaveChangesAsync();
    }

    private static SetDivisionContributionsCommandHandler CreateContributionsHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static CreateDivisionGoalCommandHandler CreateDivisionGoalHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionContributionRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new GoalRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static CreateDivisionKpiCommandHandler CreateDivisionKpiHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static GetDivisionPlanQueryHandler CreateGetPlanHandler(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new GoalRepository(lease.Context),
            CreatePeriodService(lease),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new TestRagThresholdService());

    private static DivisionPeriodCalculationService CreatePeriodService(TestDbContextLease lease)
        => new(
            new DivisionGoalRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new KpiScoreCalculator(new ScoreStrategyFactory()),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            TestBusinessClock.Real());

    // FR-904: builds a cumulative target set that satisfies the reconciliation rules
    // (monthly values sum to the quarterly target, quarterly targets sum to the annual target).
    private static async Task<List<DivisionKpiTargetInputDto>> BuildReconciledTargetsAsync(
        TestDbContextLease lease, Guid fiscalYearId)
    {
        var periods = await lease.Context.FiscalPeriods
            .Where(p => p.FiscalYearId == fiscalYearId)
            .OrderBy(p => p.FiscalMonth)
            .ToListAsync();

        var targets = periods
            .Select(p => new DivisionKpiTargetInputDto
            {
                FiscalPeriodId = p.Id,
                TargetValue = 5m,
                Granularity = TargetGranularity.Monthly
            })
            .ToList();

        targets.AddRange(periods.Where(p => p.IsQuarterEnd).Select(p => new DivisionKpiTargetInputDto
        {
            FiscalPeriodId = p.Id,
            TargetValue = 15m,
            Granularity = TargetGranularity.Quarterly
        }));

        targets.Add(new DivisionKpiTargetInputDto
        {
            FiscalPeriodId = periods.Single(p => p.IsYearEnd).Id,
            TargetValue = 60m,
            Granularity = TargetGranularity.Annual
        });

        return targets;
    }

    private static async Task<Guid> CreatePlanWithContributionsAsync(TestDbContextLease lease, Fixture fixture)
    {
        var handler = CreateContributionsHandler(lease);
        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId }
        }, CancellationToken.None);
        return response.Data!.Id;
    }

    private static readonly Guid divisionId = Guid.NewGuid();

    [Fact]
    public async Task CreateDivision_CreatesActiveDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDataInputRoleAsync(lease);
        var handler = CreateDivisionHandler(lease);

        var response = await handler.Handle(new CreateDivisionCommandRequest
        {
            Name = "Retail Operations",
            Code = "RET"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Retail Operations", response.Data!.Name);
        Assert.Equal("RET", response.Data.Code);
        Assert.True(response.Data.IsActive);

        var saved = await lease.Context.Divisions.SingleAsync();
        Assert.Equal(response.Data.Id, saved.Id);

        var account = await lease.Context.Users.SingleAsync(u => u.DivisionId == saved.Id);
        Assert.Equal("RET", account.Username);
        Assert.True(await lease.Context.UserRoles.AnyAsync(ur => ur.UserId == account.Id));
    }

    [Fact]
    public async Task CreateDivision_DuplicateName_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDataInputRoleAsync(lease);
        var handler = CreateDivisionHandler(lease);
        await handler.Handle(new CreateDivisionCommandRequest { Name = "Retail", Code = "RET" }, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionCommandRequest { Name = "Retail", Code = "RTL" }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivision_DuplicateCode_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedDataInputRoleAsync(lease);
        var handler = CreateDivisionHandler(lease);
        await handler.Handle(new CreateDivisionCommandRequest { Name = "Retail", Code = "RET" }, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionCommandRequest { Name = "Retail Ops", Code = "RET" }, CancellationToken.None));
    }

    [Fact]
    public async Task SetContributions_CreatesPlanAndLinksGoals()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var handler = CreateContributionsHandler(lease);

        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId, fixture.GoalBId }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.Contributions.Count);
        Assert.Empty(response.Data.AvailableGoals);
        Assert.Equal(DivisionPlanStatus.Draft, response.Data.PlanStatus);

        var plan = await lease.Context.DivisionPlans.SingleAsync();
        Assert.Equal(2, await lease.Context.DivisionContributions.CountAsync(c => c.DivisionPlanId == plan.Id));
    }

    [Fact]
    public async Task SetContributions_UpdatesExistingPlan()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var handler = CreateContributionsHandler(lease);

        await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId, fixture.GoalBId }
        }, CancellationToken.None);

        var response = await handler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId }
        }, CancellationToken.None);

        Assert.Single(response.Data!.Contributions);
        Assert.Equal(1, await lease.Context.DivisionPlans.CountAsync());
    }

    [Fact]
    public async Task SetContributions_DeselectGoal_KeepsDivisionGoalsByDefault()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);

        var contributionsHandler = CreateContributionsHandler(lease);
        await contributionsHandler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalAId, fixture.GoalBId }
        }, CancellationToken.None);

        var goalHandler = CreateDivisionGoalHandler(lease);
        await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 60
        }, CancellationToken.None);

        var goalId = (await lease.Context.DivisionGoals.SingleAsync()).Id;

        var response = await contributionsHandler.Handle(new SetDivisionContributionsCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            GoalIds = new List<Guid> { fixture.GoalBId }
        }, CancellationToken.None);

        Assert.Single(response.Data!.Contributions);
        Assert.Equal(fixture.GoalBId, response.Data.Contributions.Single().GoalId);
        // FR-902 keep-or-delete: without an explicit cascade flag the contribution is
        // removed but the division goal (and its KPIs) stay active in the plan.
        Assert.Contains(response.Data.Goals, g => g.Id == goalId);
        Assert.DoesNotContain(
            await lease.Context.DivisionGoals.IgnoreQueryFilters().ToListAsync(),
            g => g.Id == goalId && g.IsDeleted);
    }

    [Fact]
    public async Task SetContributions_GoalNotInScorecard_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var handler = CreateContributionsHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new SetDivisionContributionsCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                GoalIds = new List<Guid> { Guid.NewGuid() }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionGoal_CreatesGoalUnderContribution()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var handler = CreateDivisionGoalHandler(lease);

        var response = await handler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Description = "Grow branch coverage",
            Weight = 60,
            ContributionDescription = "Drives revenue growth"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Expand Retail Network", response.Data!.Name);
        Assert.Equal(fixture.GoalAId, response.Data.ParentGoalId);
        Assert.Equal("Grow Revenue", response.Data.ParentGoalName);

        var saved = await lease.Context.DivisionGoals.SingleAsync();
        Assert.Equal(60, saved.Weight);
    }

    [Fact]
    public async Task CreateDivisionGoal_WeightExceeds100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var handler = CreateDivisionGoalHandler(lease);

        await handler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 70
        }, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Boost Digital Sales",
                Weight = 40
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionGoal_NoContributions_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        var handler = CreateDivisionGoalHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 50
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionGoal_ParentNotContributed_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var handler = CreateDivisionGoalHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalBId,
                Name = "Improve NPS",
                Weight = 50
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionGoal_DuplicateName_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var handler = CreateDivisionGoalHandler(lease);

        await handler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 50
        }, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionGoalCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                ParentGoalId = fixture.GoalAId,
                Name = "Expand Retail Network",
                Weight = 30
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionKpi_CreatesKpiWithTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalHandler = CreateDivisionGoalHandler(lease);
        var goal = await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var kpiHandler = CreateDivisionKpiHandler(lease);
        var response = await kpiHandler.Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goal.Data!.Id,
            Name = "New Branches Opened",
            Unit = "count",
            Weight = 100,
            OptimizationType = KpiOptimizationType.Maximize,
            KpiType = KpiType.Lagging,
            IsCumulative = true,
            Targets = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId)
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("New Branches Opened", response.Data!.Name);
        Assert.Equal(100, response.Data.Weight);

        var savedKpi = await lease.Context.Kpis.SingleAsync();
        Assert.Equal(goal.Data.Id, savedKpi.DivisionGoalId);
        Assert.Null(savedKpi.KraId);
        Assert.Equal(fixture.ScorecardId, savedKpi.ScorecardId);

        var savedTargets = await lease.Context.KpiTargets.Where(t => t.KpiId == savedKpi.Id).ToListAsync();
        Assert.Equal(17, savedTargets.Count);
        Assert.Equal(12, savedTargets.Count(t => t.Granularity == TargetGranularity.Monthly));
        Assert.Equal(4, savedTargets.Count(t => t.Granularity == TargetGranularity.Quarterly));
        Assert.Equal(60m, savedTargets.Single(t => t.Granularity == TargetGranularity.Annual).TargetValue);
    }

    [Fact]
    public async Task CreateDivisionKpi_MissingAnnualTarget_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goal = await CreateDivisionGoalHandler(lease).Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var kpiHandler = CreateDivisionKpiHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            kpiHandler.Handle(new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goal.Data!.Id,
                Name = "New Branches",
                Weight = 100,
                Targets = new List<DivisionKpiTargetInputDto>
                {
                    new() { FiscalPeriodId = fixture.PeriodId, TargetValue = 5, Granularity = TargetGranularity.Monthly }
                }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionKpi_ReconciliationMismatch_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goal = await CreateDivisionGoalHandler(lease).Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var targets = await BuildReconciledTargetsAsync(lease, fixture.FiscalYearId);
        var quarterly = targets.First(t => t.Granularity == TargetGranularity.Quarterly);
        quarterly.TargetValue = 999m; // no longer matches the sum of its months / the annual total

        var kpiHandler = CreateDivisionKpiHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            kpiHandler.Handle(new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goal.Data!.Id,
                Name = "New Branches",
                Weight = 100,
                IsCumulative = true,
                Targets = targets
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionKpi_WeightExceeds100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalHandler = CreateDivisionGoalHandler(lease);
        var goal = await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var kpiHandler = CreateDivisionKpiHandler(lease);
        await kpiHandler.Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goal.Data!.Id,
            Name = "New Branches",
            Weight = 70
        }, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            kpiHandler.Handle(new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goal.Data.Id,
                Name = "Renewals",
                Weight = 40
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionKpi_GoalNotInPlan_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);
        var handler = CreateDivisionKpiHandler(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = Guid.NewGuid(),
                Name = "New Branches",
                Weight = 100
            }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDivisionKpi_TargetOutsideFiscalYear_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalHandler = CreateDivisionGoalHandler(lease);
        var goal = await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var kpiHandler = CreateDivisionKpiHandler(lease);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            kpiHandler.Handle(new CreateDivisionKpiCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = fixture.ScorecardId,
                DivisionGoalId = goal.Data!.Id,
                Name = "New Branches",
                Weight = 100,
                Targets = new List<DivisionKpiTargetInputDto>
                {
                    new() { FiscalPeriodId = Guid.NewGuid(), TargetValue = 5 }
                }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task GetDivisionPlan_ReturnsContributionsGoalsAndKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixture(lease);
        await CreatePlanWithContributionsAsync(lease, fixture);

        var goalHandler = CreateDivisionGoalHandler(lease);
        var goal = await goalHandler.Handle(new CreateDivisionGoalCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            ParentGoalId = fixture.GoalAId,
            Name = "Expand Retail Network",
            Weight = 100
        }, CancellationToken.None);

        var kpiHandler = CreateDivisionKpiHandler(lease);
        await kpiHandler.Handle(new CreateDivisionKpiCommandRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId,
            DivisionGoalId = goal.Data!.Id,
            Name = "New Branches",
            Weight = 100
        }, CancellationToken.None);

        var queryHandler = CreateGetPlanHandler(lease);
        var response = await queryHandler.Handle(new GetDivisionPlanQueryRequest
        {
            DivisionId = divisionId,
            ScorecardId = fixture.ScorecardId
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(fixture.GoalAId, response.Data!.Contributions.Single().GoalId);
        Assert.Contains(response.Data.AvailableGoals, g => g.GoalId == fixture.GoalBId);
        var planGoal = response.Data.Goals.Single();
        Assert.Equal("Expand Retail Network", planGoal.Name);
        Assert.Single(planGoal.Kpis);
    }

    [Fact]
    public void CreateDivisionValidator_RequiresNameAndCode()
    {
        var validator = new Application.Validation.Division.CreateDivisionValidator();
        var result = validator.Validate(new CreateDivisionCommandRequest { Name = "", Code = "" });

        Assert.False(result.IsValid);
    }
}
