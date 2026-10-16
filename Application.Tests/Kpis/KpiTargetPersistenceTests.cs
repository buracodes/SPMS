using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiTargetPersistenceTests
{
    [Fact]
    public async Task KpiTarget_MonthlyQuarterlyAnnual_PersistsCorrectly()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue Growth",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.FiscalPeriods.AddRange(periods);
        await lease.Context.SaveChangesAsync();

        var monthlyPeriod = periods.First(p => p.FiscalMonth == 1); // July
        var quarterlyPeriod = periods.First(p => p.Quarter == 1 && p.FiscalMonth == 3); // Q1 end
        var annualPeriod = periods.Last(); // June

        lease.Context.KpiTargets.AddRange(
            new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = monthlyPeriod.Id, TargetValue = 5, Granularity = TargetGranularity.Monthly },
            new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = quarterlyPeriod.Id, TargetValue = 15, Granularity = TargetGranularity.Quarterly },
            new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = annualPeriod.Id, TargetValue = 20, Granularity = TargetGranularity.Annual }
        );

        await lease.Context.SaveChangesAsync();

        var targets = await lease.Context.KpiTargets.Where(t => t.KpiId == kpi.Id).ToListAsync();

        Assert.Equal(3, targets.Count);
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Monthly && t.TargetValue == 5);
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Quarterly && t.TargetValue == 15);
        Assert.Contains(targets, t => t.Granularity == TargetGranularity.Annual && t.TargetValue == 20);
    }

    [Fact]
    public async Task KpiTarget_UniquePerKpiPeriodGranularity()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Test Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Test KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue Growth",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.FiscalPeriods.AddRange(periods);
        await lease.Context.SaveChangesAsync();

        var period = periods.First();

        lease.Context.KpiTargets.AddRange(
            new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, TargetValue = 10, Granularity = TargetGranularity.Monthly },
            new KpiTarget { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, TargetValue = 20, Granularity = TargetGranularity.Monthly }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());
    }
}