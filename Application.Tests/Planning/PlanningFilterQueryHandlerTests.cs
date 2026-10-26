using Application.CQRS.Kpi.Handlers.Queries;
using Application.CQRS.Kpi.Requests.Queries;
using Application.CQRS.Kra.Handlers.Queries;
using Application.CQRS.Kra.Requests.Queries;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;

namespace Application.Tests.Planning;

public class PlanningFilterQueryHandlerTests
{
    private sealed class Plan
    {
        public FiscalYear Year2025 { get; init; } = default!;
        public FiscalYear Year2026 { get; init; } = default!;
        public Scorecard Corporate2025 { get; init; } = default!;
        public Scorecard Corporate2026 { get; init; } = default!;
        public Goal GoalA { get; init; } = default!;
        public Goal GoalB { get; init; } = default!;
        public Kra KraA { get; init; } = default!;
        public Kra KraB { get; init; } = default!;
    }

    private static async Task<Plan> SeedAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var year2025 = calendar.CreateFiscalYear(2025);
        var year2026 = calendar.CreateFiscalYear(2026);

        var corporate2025 = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year2025.Id,
            Name = "Corporate 2025",
            Scope = ScorecardScope.Corporate
        };
        var corporate2026 = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year2026.Id,
            Name = "Corporate 2026",
            Scope = ScorecardScope.Corporate
        };

        var goalA = new Goal { Id = Guid.NewGuid(), ScorecardId = corporate2025.Id, Name = "Grow revenue", Weight = 60 };
        var goalB = new Goal { Id = Guid.NewGuid(), ScorecardId = corporate2025.Id, Name = "Retain talent", Weight = 40 };

        var kraA = new Kra
        {
            Id = Guid.NewGuid(),
            ScorecardId = corporate2025.Id,
            GoalId = goalA.Id,
            Name = "Win new logos",
            Weight = 30
        };
        var kraB = new Kra
        {
            Id = Guid.NewGuid(),
            ScorecardId = corporate2025.Id,
            GoalId = goalB.Id,
            Name = "Lower churn",
            Weight = 20
        };

        lease.Context.FiscalYears.AddRange(year2025, year2026);
        lease.Context.Scorecards.AddRange(corporate2025, corporate2026);
        lease.Context.Goals.AddRange(goalA, goalB);
        lease.Context.Kras.AddRange(kraA, kraB);
        await lease.Context.SaveChangesAsync();

        return new Plan
        {
            Year2025 = year2025,
            Year2026 = year2026,
            Corporate2025 = corporate2025,
            Corporate2026 = corporate2026,
            GoalA = goalA,
            GoalB = goalB,
            KraA = kraA,
            KraB = kraB
        };
    }

    private static Kpi Kpi(Scorecard scorecard, string name, Guid? kraId = null, decimal weight = 10)
        => new()
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kraId,
            Name = name,
            Weight = weight,
            OptimizationType = KpiOptimizationType.Maximize,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

    private static GetAllKpiQueryHandler KpiHandler(TestDbContextLease lease)
        => new(
            new KpiRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new KraRepository(lease.Context),
            new GoalRepository(lease.Context),
            new TestOrganizationalScopeService());

    private static GetAllKraQueryHandler KraHandler(TestDbContextLease lease)
        => new(
            new KraRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KpiRepository(lease.Context));

    [Fact]
    public async Task KpiList_ResolvesScorecardKraAndGoalNames()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "New ARR", plan.KraA.Id));
        await lease.Context.SaveChangesAsync();

        var response = await KpiHandler(lease).Handle(new GetAllKpiQueryRequest(), CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("Corporate 2025", row.ScorecardName);
        Assert.Equal("Win new logos", row.KraName);
        Assert.Equal(plan.GoalA.Id, row.GoalId);
        Assert.Equal("Grow revenue", row.GoalName);
    }

    [Fact]
    public async Task KpiList_LeavesNamesNullWhenTheKpisHierarchyIsIncomplete()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        // A KPI whose parent KRA has been soft-deleted (the runtime "hierarchy
        // incomplete" state) resolves no KRA and therefore no goal name.
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Standalone KPI", plan.KraA.Id));
        var parentKra = await lease.Context.Kras.FindAsync(plan.KraA.Id);
        parentKra!.IsDeleted = true;
        await lease.Context.SaveChangesAsync();

        var response = await KpiHandler(lease).Handle(new GetAllKpiQueryRequest(), CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("Corporate 2025", row.ScorecardName);
        Assert.Null(row.KraName);
        Assert.Null(row.GoalId);
        Assert.Null(row.GoalName);
    }

    [Fact]
    public async Task KpiList_FiltersByFiscalYearThroughTheScorecard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "In 2025", plan.KraA.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2026, "In 2026", plan.KraA.Id));
        await lease.Context.SaveChangesAsync();

        var response = await KpiHandler(lease)
            .Handle(new GetAllKpiQueryRequest { FiscalYearId = plan.Year2026.Id }, CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("In 2026", row.Name);
        Assert.Equal("Corporate 2026", row.ScorecardName);
    }

    [Fact]
    public async Task KpiList_FiltersByGoalThroughTheKra()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Under goal A", plan.KraA.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Under goal B", plan.KraB.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Under goal A also", plan.KraA.Id));
        await lease.Context.SaveChangesAsync();

        var response = await KpiHandler(lease)
            .Handle(new GetAllKpiQueryRequest { GoalId = plan.GoalB.Id }, CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("Under goal B", row.Name);
    }

    [Fact]
    public async Task KpiList_CombinesFiscalYearGoalAndKraFilters()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Target row", plan.KraA.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Other goal", plan.KraB.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2026, "Other year", plan.KraA.Id));
        await lease.Context.SaveChangesAsync();

        var response = await KpiHandler(lease).Handle(
            new GetAllKpiQueryRequest
            {
                FiscalYearId = plan.Year2025.Id,
                GoalId = plan.GoalA.Id,
                KraId = plan.KraA.Id
            },
            CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("Target row", row.Name);
    }

    [Fact]
    public async Task KraList_ResolvesScorecardAndGoalNames()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);

        var response = await KraHandler(lease).Handle(new GetAllKraQueryRequest(), CancellationToken.None);

        Assert.Equal(2, response.Data!.Count);
        var row = Assert.Single(response.Data!, k => k.Id == plan.KraA.Id);
        Assert.Equal("Corporate 2025", row.ScorecardName);
        Assert.Equal("Grow revenue", row.GoalName);
    }

    [Fact]
    public async Task KraList_ReportsTheNumberOfKpisUnderEachKra()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "One", plan.KraA.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Two", plan.KraA.Id));
        lease.Context.Kpis.Add(Kpi(plan.Corporate2025, "Three", plan.KraB.Id));
        await lease.Context.SaveChangesAsync();

        var response = await KraHandler(lease).Handle(new GetAllKraQueryRequest(), CancellationToken.None);

        Assert.Equal(2, response.Data!.Single(k => k.Id == plan.KraA.Id).KpiCount);
        Assert.Equal(1, response.Data!.Single(k => k.Id == plan.KraB.Id).KpiCount);
    }

    [Fact]
    public async Task KraList_FiltersByFiscalYearAndGoal()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var plan = await SeedAsync(lease);
        var otherYearKra = new Kra
        {
            Id = Guid.NewGuid(),
            ScorecardId = plan.Corporate2026.Id,
            GoalId = Guid.NewGuid(),
            Name = "Next year KRA",
            Weight = 10
        };
        lease.Context.Goals.Add(new Goal { Id = otherYearKra.GoalId, ScorecardId = plan.Corporate2026.Id, Name = "Next year goal", Weight = 100 });
        lease.Context.Kras.Add(otherYearKra);
        await lease.Context.SaveChangesAsync();

        var byYear = await KraHandler(lease)
            .Handle(new GetAllKraQueryRequest { FiscalYearId = plan.Year2025.Id }, CancellationToken.None);
        Assert.Equal(2, byYear.Data!.Count);
        Assert.DoesNotContain(byYear.Data!, k => k.Id == otherYearKra.Id);

        var byGoal = await KraHandler(lease)
            .Handle(new GetAllKraQueryRequest { GoalId = plan.GoalB.Id }, CancellationToken.None);
        var row = Assert.Single(byGoal.Data!);
        Assert.Equal("Lower churn", row.Name);
    }
}
