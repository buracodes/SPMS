using Application.CQRS.KpiActuals.Requests.Commands;
using Application.CQRS.KpiActuals.Handlers.Commands;
using Application.DTOS.KpiActual;
using Application.Interface.IRepository;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiTargetActualLifecycleTests
{
    private static (Guid FiscalYearId, Guid PeriodId, Guid KpiId) SeedKpi(TestDbContextLease lease, bool isCumulative = true)
    {
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        var periods = service.GeneratePeriods(fiscalYear);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Corporate 2025" };
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

        return (fiscalYear.Id, periods[0].Id, kpi.Id);
    }

    private static EnterActualValueCommandRequest ActualRequest(Guid kpiId, Guid periodId, decimal value)
        => new()
        {
            Dto = new EnterActualValueDto
            {
                KpiId = kpiId,
                FiscalPeriodId = periodId,
                Value = value
            }
        };

    private static EnterActualValueCommandHandler ActualHandler(TestDbContextLease lease)
        => new(
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new UserRepository(lease.Context),
            new TestCurrentUserService(),
            new TestSpmsJobScheduler(),
            new Application.Services.AuditContext(),
            new TestEffectivePermissionService(),
            new SpmsUnitOfWork(lease.Context));

    // â”€â”€ FR-309 day-dedupe â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task ManualHttpWrite_SupersedesPriorAutomatedRow_WithoutDeletingIt()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, periodId, kpiId) = SeedKpi(lease);

        var priorId = Guid.NewGuid();
        lease.Context.Set<KpiActualValue>().Add(new KpiActualValue
        {
            Id = priorId,
            KpiId = kpiId,
            FiscalPeriodId = periodId,
            Value = 7m,
            DataSource = KpiDataSource.Automated,
            AsOfDate = DateTime.UtcNow.AddHours(-3),
            IsCurrent = true
        });
        lease.Context.SaveChanges();

        var response = await ActualHandler(lease).Handle(ActualRequest(kpiId, periodId, 9m), CancellationToken.None);

        var rows = await lease.Context.KpiActualValues.IgnoreQueryFilters()
            .Where(v => v.KpiId == kpiId && v.FiscalPeriodId == periodId)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        var prior = Assert.Single(rows, r => r.Id == priorId);
        Assert.False(prior.IsCurrent);
        Assert.False(prior.IsDeleted);
        Assert.Equal(priorId, response.Data!.SupersedesActualId);
        Assert.Equal(priorId, Assert.Single(rows, r => r.IsCurrent).SupersedesActualId);
        Assert.Equal(KpiDataSource.Manual, response.Data.DataSource);

        // Filtered reads show only the one current revision.
        var visible = await lease.Context.KpiActualValues.Where(v => v.KpiId == kpiId).ToListAsync();
        Assert.Single(visible, r => r.Value == 9m);
    }

    [Fact]
    public async Task ManualWriteSupersedesPriorManualRow_AppendOnly()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, periodId, kpiId) = SeedKpi(lease);

        lease.Context.Set<KpiActualValue>().Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpiId,
            FiscalPeriodId = periodId,
            Value = 3m,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow.AddHours(-1),
            IsCurrent = true
        });
        lease.Context.SaveChanges();

        await ActualHandler(lease).Handle(ActualRequest(kpiId, periodId, 4m), CancellationToken.None);

        var rows = await lease.Context.KpiActualValues.IgnoreQueryFilters()
            .Where(v => v.KpiId == kpiId && v.FiscalPeriodId == periodId)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.IsDeleted);
        var current = Assert.Single(rows, r => r.IsCurrent && r.Value == 4m);
        Assert.NotNull(current.SupersedesActualId);
        Assert.Equal(current.SupersedesActualId, Assert.Single(rows, r => !r.IsCurrent).Id);
    }

    // â”€â”€ Historical target preservation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task StaleTargetCombination_IsSoftDeletedAndRecreatable()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var (_, _, kpiId) = SeedKpi(lease);

        var repository = new KpiTargetRepository(lease.Context);

        // First write: quarterly Q1..Q3 keys only (staged).
        var firstSet = new List<KpiTarget>
        {
            new() { Id = Guid.NewGuid(), KpiId = kpiId, FiscalPeriodId = lease.Context.FiscalPeriods.First(p => p.IsYearEnd).Id, TargetValue = 90m, Granularity = TargetGranularity.Annual }
        };
        await repository.ReplaceForFiscalYearAsync(kpiId, lease.Context.FiscalYears.First().Id, firstSet);

        // Second write: same annual key recreated after the first one is deactivated.
        firstSet[0].IsDeleted = true;
        await lease.Context.SaveChangesAsync();

        var overwrite = new List<KpiTarget>
        {
            new() { Id = Guid.NewGuid(), KpiId = kpiId, FiscalPeriodId = lease.Context.FiscalPeriods.First(p => p.IsYearEnd).Id, TargetValue = 120m, Granularity = TargetGranularity.Annual }
        };
        await repository.ReplaceForFiscalYearAsync(kpiId, lease.Context.FiscalYears.First().Id, overwrite);
        await lease.Context.SaveChangesAsync();

        var allRows = await lease.Context.KpiTargets.IgnoreQueryFilters()
            .Where(t => t.KpiId == kpiId)
            .ToListAsync();
        Assert.Equal(2, allRows.Count);
        Assert.Single(allRows, t => t.IsDeleted && t.TargetValue == 90m);
        Assert.Single(allRows, t => !t.IsDeleted && t.TargetValue == 120m);
    }
}
