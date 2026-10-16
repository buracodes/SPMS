using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiActualValuePersistenceTests
{
    [Fact]
    public async Task KpiActualValue_LatestWriteWins_ByAsOfDate()
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
        var asOf1 = DateTime.UtcNow.AddMinutes(-10);
        var asOf2 = DateTime.UtcNow.AddMinutes(-5);
        var asOf3 = DateTime.UtcNow;

        // Manual entry first
        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 10,
            DataSource = KpiDataSource.Manual,
            AsOfDate = asOf1
        });

        // Automated overwrite
        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 12,
            DataSource = KpiDataSource.Automated,
            AsOfDate = asOf2
        });

        // Manual correction after automated
        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 11,
            DataSource = KpiDataSource.Manual,
            AsOfDate = asOf3
        });

        await lease.Context.SaveChangesAsync();

        // Latest write wins regardless of source
        var latestManual = await lease.Context.KpiActualValues
            .Where(a => a.KpiId == kpi.Id && a.FiscalPeriodId == period.Id && a.DataSource == KpiDataSource.Manual)
            .OrderByDescending(a => a.AsOfDate)
            .FirstAsync();

        var latestAutomated = await lease.Context.KpiActualValues
            .Where(a => a.KpiId == kpi.Id && a.FiscalPeriodId == period.Id && a.DataSource == KpiDataSource.Automated)
            .OrderByDescending(a => a.AsOfDate)
            .FirstAsync();

        Assert.Equal(11, latestManual.Value); // Latest manual write
        Assert.Equal(12, latestAutomated.Value); // Latest automated write
    }

    [Fact]
    public async Task KpiActualValue_GetLatestBySource_RepositoryMethod()
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

        // Add multiple entries for same source
        lease.Context.KpiActualValues.AddRange(
            new KpiActualValue { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, Value = 5, DataSource = KpiDataSource.Manual, AsOfDate = DateTime.UtcNow.AddDays(-2) },
            new KpiActualValue { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, Value = 7, DataSource = KpiDataSource.Manual, AsOfDate = DateTime.UtcNow.AddDays(-1) },
            new KpiActualValue { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, Value = 6, DataSource = KpiDataSource.Automated, AsOfDate = DateTime.UtcNow }
        );

        await lease.Context.SaveChangesAsync();

        // Use repository to get latest by source
        var repository = new Infrastructure.Persistence.Repositories.KpiActualValueRepository(lease.Context);

        var latestManual = await repository.GetLatestAsync(kpi.Id, period.Id, KpiDataSource.Manual);
        var latestAutomated = await repository.GetLatestAsync(kpi.Id, period.Id, KpiDataSource.Automated);

        Assert.NotNull(latestManual);
        Assert.Equal(7, latestManual.Value); // Latest manual
        Assert.Equal(DateTime.UtcNow.AddDays(-1).Date, latestManual.AsOfDate.Date);

        Assert.NotNull(latestAutomated);
        Assert.Equal(6, latestAutomated.Value); // Latest automated
    }
}