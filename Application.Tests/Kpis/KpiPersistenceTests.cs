using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiPersistenceTests
{
    [Fact]
    public async Task Kpi_WithScorecardAndKra_PersistsCorrectly()
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
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            GoalId = goal.Id,
            Name = "Test KRA",
            Weight = 50
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        await lease.Context.SaveChangesAsync();

        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue Growth",
            Unit = "%",
            Weight = 100,
            OptimizationType = KpiOptimizationType.Maximize,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging,
            IsCumulative = false,
            Baseline = 10,
            MinValue = 0,
            MaxValue = 100
        };

        lease.Context.Kpis.Add(kpi);
        await lease.Context.SaveChangesAsync();

        var persisted = await lease.Context.Kpis
            .Include(item => item.Kra)
            .Include(item => item.Scorecard)
            .SingleAsync();

        Assert.Equal("Revenue Growth", persisted.Name);
        Assert.Equal("%", persisted.Unit);
        Assert.Equal(100, persisted.Weight);
        Assert.Equal(KpiOptimizationType.Maximize, persisted.OptimizationType);
        Assert.Equal(KpiDataSource.Manual, persisted.DataSource);
        Assert.Equal(KpiType.Lagging, persisted.KpiType);
        Assert.Equal(10, persisted.Baseline);
        Assert.Equal(0, persisted.MinValue);
        Assert.Equal(100, persisted.MaxValue);
        Assert.Equal(kra.Id, persisted.KraId);
        Assert.Equal(scorecard.Id, persisted.ScorecardId);
    }

    [Fact]
    public async Task Kpi_NameMustBeUniqueWithinKra()
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
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        await lease.Context.SaveChangesAsync();

        lease.Context.Kpis.AddRange(
            NewKpiUnderKra(scorecard.Id, kra.Id, "Revenue Growth"),
            NewKpiUnderKra(scorecard.Id, kra.Id, "Revenue Growth"));

        await Assert.ThrowsAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Kpi_SameNameAllowedAcrossDifferentKras()
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
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kraA = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "KRA A", Weight = 50 };
        var kraB = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "KRA B", Weight = 50 };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.AddRange(kraA, kraB);
        await lease.Context.SaveChangesAsync();

        lease.Context.Kpis.AddRange(
            NewKpiUnderKra(scorecard.Id, kraA.Id, "Customer Satisfaction"),
            NewKpiUnderKra(scorecard.Id, kraB.Id, "Customer Satisfaction"));

        await lease.Context.SaveChangesAsync();

        Assert.Equal(2, await lease.Context.Kpis.CountAsync());
    }

    [Fact]
    public async Task Kpi_SameNameAllowedAcrossDifferentScorecards()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);

        var scorecard1 = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Scorecard A", Scope = ScorecardScope.Corporate };
        var scorecard2 = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Scorecard B", Scope = ScorecardScope.Division };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.AddRange(scorecard1, scorecard2);
        await lease.Context.SaveChangesAsync();

        lease.Context.Kpis.AddRange(
            NewKpi(scorecard1.Id, "Revenue Growth"),
            NewKpi(scorecard2.Id, "Revenue Growth"));

        await lease.Context.SaveChangesAsync();

        Assert.Equal(2, await lease.Context.Kpis.CountAsync());
    }

    [Fact]
    public async Task Kpi_ChildKpi_RollsUpToParent()
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
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        await lease.Context.SaveChangesAsync();

        var parentKpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Total Revenue",
            Unit = "ETB",
            Weight = 100,
            DataSource = KpiDataSource.Calculated
        };

        var childKpi1 = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue Division A",
            Unit = "ETB",
            Weight = 60,
            ParentKpiId = parentKpi.Id,
            DataSource = KpiDataSource.Manual
        };

        var childKpi2 = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue Division B",
            Unit = "ETB",
            Weight = 40,
            ParentKpiId = parentKpi.Id,
            DataSource = KpiDataSource.Manual
        };

        lease.Context.Kpis.AddRange(parentKpi, childKpi1, childKpi2);
        await lease.Context.SaveChangesAsync();

        var parent = await lease.Context.Kpis.Include(p => p.ChildKpis).SingleAsync(k => k.Id == parentKpi.Id);

        Assert.Equal(2, parent.ChildKpis.Count);
        Assert.Contains(parent.ChildKpis, c => c.Name == "Revenue Division A");
        Assert.Contains(parent.ChildKpis, c => c.Name == "Revenue Division B");
    }

    private static Kpi NewKpi(Guid scorecardId, string name)
    {
        return new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            Name = name,
            Unit = "%",
            Weight = 50,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
    }

    private static Kpi NewKpiUnderKra(Guid scorecardId, Guid kraId, string name)
    {
        return new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            KraId = kraId,
            Name = name,
            Unit = "%",
            Weight = 50,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
    }
}