using Application.CQRS.KpiTargets.Requests.Commands;
using Application.CQRS.KpiTargets.Handlers.Commands;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiTargetReconciliationRuleTests
{
    private static (Guid FiscalYearId, Guid KpiId, FiscalYear Year) SeedKpi(TestDbContextLease lease, bool isCumulative, decimal baseline = 0m, decimal? minValue = null)
    {
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Corporate 2025", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow volume", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Win mandates", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Traded volume",
            Weight = 100,
            IsCumulative = isCumulative,
            Baseline = baseline,
            MinValue = minValue,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.SaveChanges();

        return (fiscalYear.Id, kpi.Id, fiscalYear);
    }

    private static SetKpiTargetsCommandHandler CreateHandler(TestDbContextLease lease)
        => new(
            new KpiRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new FiscalCalendarService(),
            new TestSpmsJobScheduler(),
            new Infrastructure.Persistence.SpmsUnitOfWork(lease.Context));

    private static SetKpiTargetsDto Dto(Guid kpiId, Guid fiscalYearId, List<decimal>? months, List<decimal>? quarters, decimal annual)
        => new()
        {
            KpiId = kpiId,
            FiscalYearId = fiscalYearId,
            MonthlyTargets = months ?? new List<decimal>(),
            QuarterlyTargets = quarters,
            AnnualTarget = annual
        };

    [Fact]
    public async Task Cumulative_MonthlySumBreaksQuarterlyTarget_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: true);
        var months = Enumerable.Repeat(26m, 12).ToList();
        var quarters = new List<decimal> { 75m, 75m, 75m, 75m };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 300m) }, CancellationToken.None));
        Assert.Contains("Q1", ex.Message);
    }

    [Fact]
    public async Task Cumulative_QuarterlySumBreaksAnnualTarget_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: true);
        var months = Enumerable.Repeat(25m, 12).ToList();
        var quarters = new List<decimal> { 75m, 75m, 75m, 75m };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 299m) }, CancellationToken.None));
        Assert.Contains("quart", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cumulative_WithinTolerance_AcceptsThreeThousandths()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: true);
        var months = new List<decimal> { 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.0m, 1.002m };
        var quarters = new List<decimal> { 3m, 3m, 3m, 3m };

        var response = await CreateHandler(lease).Handle(
            new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 12.002m) }, CancellationToken.None);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task NonCumulative_MonthlyTargetMustEqualAnnual_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: false);
        var months = Enumerable.Repeat(50m, 12).ToList();
        var quarters = new List<decimal> { 50m, 50m, 50m, 50m };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 300m) }, CancellationToken.None));
        Assert.Contains("annual target", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonCumulative_AllRowsEqualAnnual_Saves()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: false);
        var months = Enumerable.Repeat(50m, 12).ToList();
        var quarters = new List<decimal> { 50m, 50m, 50m, 50m };

        var response = await CreateHandler(lease).Handle(
            new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 50m) }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(17, response.Data!.Targets.Count);
    }

    [Fact]
    public async Task Cumulative_MissingQuarterlyRows_AreDerivedFromMonthlyTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: true);
        var months = Enumerable.Range(1, 12).Select(m => (decimal)m).ToList();

        var response = await CreateHandler(lease).Handle(
            new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, null, 78m) }, CancellationToken.None);

        Assert.True(response.Success);
        var targets = await lease.Context.KpiTargets.Where(t => t.KpiId == kpiId).ToListAsync();
        Assert.Equal(17, targets.Count);
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Quarterly && t.TargetValue == 6m);
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Annual && t.TargetValue == 78m);
    }

    [Fact]
    public async Task FirstConfiguration_AppliesBaselineEqualsMin_AndMaxEqualsAnnualDefaults()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: true, baseline: 5m);
        var months = Enumerable.Repeat(25m, 12).ToList();
        var quarters = new List<decimal> { 75m, 75m, 75m, 75m };

        var response = await CreateHandler(lease).Handle(
            new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 300m) }, CancellationToken.None);
        Assert.True(response.Success);

        var saved = await lease.Context.Kpis.SingleAsync(k => k.Id == kpiId);
        Assert.Equal(5m, saved.Baseline);
        Assert.Equal(300m, saved.MaxValue);
    }

    [Fact]
    public async Task ExplicitBaselineAndMax_AreNotOverwrittenByDefaults()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (fiscalYearId, kpiId, _) = SeedKpi(lease, isCumulative: true, baseline: 2m, minValue: 1m);
        var kpi = await lease.Context.Kpis.SingleAsync(k => k.Id == kpiId);
        kpi.MaxValue = 999m;
        lease.Context.SaveChanges();

        var months = Enumerable.Repeat(25m, 12).ToList();
        var quarters = new List<decimal> { 75m, 75m, 75m, 75m };

        await CreateHandler(lease).Handle(
            new SetKpiTargetsCommandRequest { Dto = Dto(kpiId, fiscalYearId, months, quarters, 300m) }, CancellationToken.None);

        var saved = await lease.Context.Kpis.SingleAsync(k => k.Id == kpiId);
        Assert.Equal(2m, saved.Baseline);
        Assert.Equal(999m, saved.MaxValue);
    }
}
