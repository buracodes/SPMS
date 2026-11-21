using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiActualValuePersistenceTests
{
    [Fact]
    public async Task KpiActualValue_CurrentReadIgnoresSupersededRevisions()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard" };
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
        var priorId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        lease.Context.KpiActualValues.AddRange(
            new KpiActualValue
            {
                Id = priorId,
                KpiId = kpi.Id,
                FiscalPeriodId = period.Id,
                Value = 10,
                DataSource = KpiDataSource.Manual,
                AsOfDate = DateTime.UtcNow.AddMinutes(-10),
                IsCurrent = false
            },
            new KpiActualValue
            {
                Id = currentId,
                KpiId = kpi.Id,
                FiscalPeriodId = period.Id,
                Value = 11,
                DataSource = KpiDataSource.Manual,
                AsOfDate = DateTime.UtcNow,
                IsCurrent = true,
                SupersedesActualId = priorId
            });
        await lease.Context.SaveChangesAsync();

        var repository = new KpiActualValueRepository(lease.Context);

        var current = await repository.GetLatestAsync(kpi.Id, period.Id);
        Assert.NotNull(current);
        Assert.Equal(11, current.Value);

        var history = await repository.GetByKpiAndPeriodAsync(kpi.Id, period.Id);
        Assert.Equal(2, history.Count);
        Assert.All(history, row => Assert.False(row.IsDeleted));
        Assert.Equal(priorId, Assert.Single(history, row => row.IsCurrent).SupersedesActualId);
    }

    [Fact]
    public async Task KpiActualValue_RepositoryReturnsExactlyOneCurrentRevision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard" };
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
        var secondId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        lease.Context.KpiActualValues.AddRange(
            new KpiActualValue { Id = Guid.NewGuid(), KpiId = kpi.Id, FiscalPeriodId = period.Id, Value = 5, DataSource = KpiDataSource.Manual, AsOfDate = DateTime.UtcNow.AddDays(-2), IsCurrent = false },
            new KpiActualValue { Id = secondId, KpiId = kpi.Id, FiscalPeriodId = period.Id, Value = 6, DataSource = KpiDataSource.Automated, AsOfDate = DateTime.UtcNow.AddDays(-1), IsCurrent = false },
            new KpiActualValue { Id = currentId, KpiId = kpi.Id, FiscalPeriodId = period.Id, Value = 7, DataSource = KpiDataSource.Manual, AsOfDate = DateTime.UtcNow, IsCurrent = true, SupersedesActualId = secondId });
        await lease.Context.SaveChangesAsync();

        var repository = new KpiActualValueRepository(lease.Context);

        var current = await repository.GetLatestAsync(kpi.Id, period.Id);
        Assert.NotNull(current);
        Assert.Equal(7, current.Value);
        Assert.Equal(KpiDataSource.Manual, current.DataSource);
        Assert.Equal(secondId, current.SupersedesActualId);
        Assert.Equal(1, await lease.Context.KpiActualValues.CountAsync(a => a.IsCurrent));

        var forYear = await repository.GetForFiscalYearAsync(kpi.Id, fiscalYear.Id);
        Assert.Single(forYear, a => a.Id == currentId);
        Assert.DoesNotContain(forYear, a => a.DataSource == KpiDataSource.Automated);
    }

    [Fact]
    public async Task FilteredUniqueIndex_RejectsSecondCurrentRevision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Test Scorecard" };
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
        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 1,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow,
            IsCurrent = true
        });
        await lease.Context.SaveChangesAsync();

        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 2,
            DataSource = KpiDataSource.Automated,
            AsOfDate = DateTime.UtcNow,
            IsCurrent = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => lease.Context.SaveChangesAsync());
    }
}
