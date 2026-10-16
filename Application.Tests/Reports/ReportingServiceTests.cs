using Application.DTOS.Report;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Reports;

public class ReportingServiceTests
{
    private static ReportingService CreateService(TestDbContextLease lease)
    {
        return new ReportingService(
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KraRepository(lease.Context),
            new KpiRepository(lease.Context),
            new KpiScoreSnapshotRepository(lease.Context),
            new KraScoreSnapshotRepository(lease.Context),
            new GoalScoreSnapshotRepository(lease.Context),
            new ScorecardScoreSnapshotRepository(lease.Context),
            new InitiativeRepository(lease.Context),
            new UserRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionReportService(
                new DivisionRepository(lease.Context),
                new DivisionPlanRepository(lease.Context),
                new DivisionGoalRepository(lease.Context),
                new KpiRepository(lease.Context),
                new FiscalPeriodRepository(lease.Context),
                new KpiTargetRepository(lease.Context),
                new KpiActualValueRepository(lease.Context),
                new DivisionScoreSnapshotRepository(lease.Context)),
            new InitiativeProgressCalculator());
    }

    private static FiscalYear CreateYear(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2026);
        var periods = calendar.GeneratePeriods(year);
        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(periods);
        return year;
    }

    private static DivisionReportService CreateDivisionReportService(TestDbContextLease lease)
        => new(
            new DivisionRepository(lease.Context),
            new DivisionPlanRepository(lease.Context),
            new DivisionGoalRepository(lease.Context),
            new KpiRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new DivisionScoreSnapshotRepository(lease.Context));

    private static async Task<(Guid ScorecardId, Guid GoalId, Guid KraId, Guid KpiId, Guid Month1, Guid Month2)> SeedAsync(
        TestDbContextLease lease, FiscalYear year)
    {
        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "ECX Corporate Strategy 2026", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Sales Growth", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue",
            Weight = 100,
            Unit = "ETB",
            IsCumulative = false,
            IsActive = true,
            OptimizationType = KpiOptimizationType.Maximize,
            KpiType = KpiType.Lagging
        };

        var periods = lease.Context.FiscalPeriods.Local.Where(p => p.FiscalYearId == year.Id).OrderBy(p => p.FiscalMonth).ToList();
        Guid month1 = periods[0].Id;
        Guid month2 = periods[1].Id;

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);

        lease.Context.KpiScoreSnapshots.Add(new KpiScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KpiId = kpi.Id,
            FiscalPeriodId = month1,
            Granularity = SnapshotGranularity.Month,
            Score = 100,
            ActualValue = 120,
            TargetValue = 100,
            CalculatedAt = new DateTime(2026, 1, 15)
        });
        lease.Context.KpiScoreSnapshots.Add(new KpiScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KpiId = kpi.Id,
            FiscalPeriodId = month2,
            Granularity = SnapshotGranularity.Month,
            Score = 80,
            ActualValue = 90,
            TargetValue = 100,
            CalculatedAt = new DateTime(2026, 2, 15)
        });

        lease.Context.KraScoreSnapshots.Add(new KraScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            FiscalPeriodId = month1,
            Granularity = SnapshotGranularity.Month,
            Score = 100,
            CalculatedAt = new DateTime(2026, 1, 15)
        });

        await lease.Context.SaveChangesAsync();

        return (scorecard.Id, goal.Id, kra.Id, kpi.Id, month1, month2);
    }

    [Fact]
    public async Task ScorecardPerformance_KpiLevel_PopulatesScoreRagTrend()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = CreateYear(lease);
        var ids = await SeedAsync(lease, year);
        var service = CreateService(lease);

        var result = await service.GenerateAsync(new ReportFilter
        {
            ReportType = "scorecard-performance",
            ScorecardId = ids.ScorecardId,
            FiscalYearId = year.Id,
            Level = "kpi",
            Granularity = "month"
        }, cancellationToken: CancellationToken.None);

        Assert.Equal(12, result.Rows.Count);
        var month1Row = result.Rows.Single(r => r[3] == "M1");
        var month2Row = result.Rows.Single(r => r[3] == "M2");

        // Score at index 6, RAG at 7, Trend at 8.
        Assert.Equal("100", month1Row[6]);
        Assert.Equal("Green", month1Row[7]);
        Assert.Equal("80", month2Row[6]);
        Assert.Equal("Amber", month2Row[7]);
        Assert.Equal("-20", month2Row[8]);
    }

    [Fact]
    public async Task ScorecardPerformance_KraLevel_PopulatesScoreRagTrend()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = CreateYear(lease);
        var ids = await SeedAsync(lease, year);
        var service = CreateService(lease);

        var result = await service.GenerateAsync(new ReportFilter
        {
            ReportType = "scorecard-performance",
            ScorecardId = ids.ScorecardId,
            FiscalYearId = year.Id,
            Level = "kra",
            Granularity = "month"
        }, cancellationToken: CancellationToken.None);

        var month1Row = result.Rows.Single(r => r[3] == "M1");
        Assert.Equal("100", month1Row[4]);
        Assert.Equal("Green", month1Row[5]);
    }

    [Fact]
    public async Task GenerateScorecardPerformance_KpiLevel_ExportCsvIncludesValues()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = CreateYear(lease);
        var ids = await SeedAsync(lease, year);
        var service = CreateService(lease);

        var bytes = await service.ExportAsync(new ReportFilter
        {
            ReportType = "scorecard-performance",
            ScorecardId = ids.ScorecardId,
            FiscalYearId = year.Id,
            Level = "kpi",
            Granularity = "month"
        }, ReportExportFormat.Csv, cancellationToken: CancellationToken.None);

        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Green", csv);
        Assert.Contains("100", csv);
    }

    [Fact]
    public async Task RecalculateScorecard_CreatesKpiSnapshots_SoReportShowsScoreRag()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = CreateYear(lease);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "ECX 2026", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Sales", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "Revenue",
            Weight = 100,
            IsActive = true,
            OptimizationType = KpiOptimizationType.Maximize
        };

        var period = lease.Context.FiscalPeriods.Local.OrderBy(p => p.FiscalMonth).First();

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.KpiTargets.Add(new KpiTarget
        {
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            TargetValue = 100,
            Granularity = TargetGranularity.Monthly
        });
        lease.Context.KpiActualValues.Add(new KpiActualValue
        {
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 120,
            DataSource = KpiDataSource.Manual,
            AsOfDate = new DateTime(2026, 1, 10)
        });
        await lease.Context.SaveChangesAsync();

        var calc = new ScoreCalculationService(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new GoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new KpiScoreSnapshotRepository(lease.Context),
            new KraScoreSnapshotRepository(lease.Context),
            new GoalScoreSnapshotRepository(lease.Context),
            new ScorecardScoreSnapshotRepository(lease.Context),
            new KpiScoreCalculator(new ScoreStrategyFactory()),
            new KraScoreCalculator(),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator());

        await calc.RecalculateScorecardAsync(scorecard.Id, period.Id);

        var snapshot = await lease.Context.KpiScoreSnapshots.SingleAsync();
        Assert.Equal(120, snapshot.Score);
        Assert.Equal(period.Id, snapshot.FiscalPeriodId);

        var service = CreateService(lease);
        var result = await service.GenerateAsync(new ReportFilter
        {
            ReportType = "scorecard-performance",
            ScorecardId = scorecard.Id,
            FiscalYearId = year.Id,
            Level = "kpi",
            Granularity = "month"
        }, cancellationToken: CancellationToken.None);

        var month1Row = result.Rows.Single(r => r[3] == "M1");
        Assert.Equal("120", month1Row[6]);
        Assert.Equal("Green", month1Row[7]);
    }

    [Fact]
    public async Task ScorecardPerformance_KpiLevel_FiltersByQuarterAndMonthRange()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = CreateYear(lease);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "ECX 2026", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), GoalId = goal.Id, ScorecardId = scorecard.Id, Name = "Sales", Weight = 100 };
        var kpi = new Kpi { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, KraId = kra.Id, Name = "Revenue", Weight = 100, IsActive = true };

        var month2 = lease.Context.FiscalPeriods.Local.Single(p => p.FiscalMonth == 2).Id;
        var month4 = lease.Context.FiscalPeriods.Local.Single(p => p.FiscalMonth == 4).Id;

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.KpiScoreSnapshots.Add(new KpiScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KpiId = kpi.Id,
            FiscalPeriodId = month2,
            Granularity = SnapshotGranularity.Month,
            Score = 80,
            ActualValue = 80,
            TargetValue = 100,
            CalculatedAt = new DateTime(2026, 2, 15)
        });
        lease.Context.KpiScoreSnapshots.Add(new KpiScoreSnapshot
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KpiId = kpi.Id,
            FiscalPeriodId = month4,
            Granularity = SnapshotGranularity.Month,
            Score = 70,
            ActualValue = 70,
            TargetValue = 100,
            CalculatedAt = new DateTime(2026, 4, 15)
        });
        await lease.Context.SaveChangesAsync();

        var service = CreateService(lease);

        // Quarter 2 → months M4..M6 only.
        var q2 = await service.GenerateAsync(new ReportFilter
        {
            ReportType = "scorecard-performance",
            ScorecardId = scorecard.Id,
            FiscalYearId = year.Id,
            Level = "kpi",
            Granularity = "month",
            Quarter = 2
        }, cancellationToken: CancellationToken.None);

        Assert.Equal(3, q2.Rows.Count);
        Assert.All(q2.Rows, r => Assert.Contains(r[3], new[] { "M4", "M5", "M6" }));
        Assert.Equal("70", q2.Rows.Single(r => r[3] == "M4")[6]);

        // Month range M1..M3.
        var range = await service.GenerateAsync(new ReportFilter
        {
            ReportType = "scorecard-performance",
            ScorecardId = scorecard.Id,
            FiscalYearId = year.Id,
            Level = "kpi",
            Granularity = "month",
            FromMonth = 1,
            ToMonth = 3
        }, cancellationToken: CancellationToken.None);

        Assert.Equal(3, range.Rows.Count);
        Assert.All(range.Rows, r => Assert.Contains(r[3], new[] { "M1", "M2", "M3" }));
        Assert.Equal("80", range.Rows.Single(r => r[3] == "M2")[6]);
    }

    [Fact]
    public async Task DivisionReport_MonthRow_UsesMonthlyTargetOnQuarterEndPeriod()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = CreateYear(lease);

        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "ECX 2026", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow Revenue", Weight = 100 };
        var division = new Domain.Entities.Division { Id = Guid.NewGuid(), Name = "Retail Operations", Code = "RET" };
        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = division.Id,
            ScorecardId = scorecard.Id,
            PlanStatus = DivisionPlanStatus.Draft
        };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = goal.Id,
            Name = "Expand Retail Network",
            Weight = 100
        };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            DivisionGoalId = divisionGoal.Id,
            Name = "New Branches",
            Weight = 100,
            IsActive = true,
            IsCumulative = false
        };

        // March is a quarter-end period: it holds both a Monthly and a Quarterly target.
        var march = lease.Context.FiscalPeriods.Local.Single(p => p.FiscalMonth == 3);

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Divisions.Add(division);
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        lease.Context.Kpis.Add(kpi);
        lease.Context.KpiTargets.Add(new KpiTarget
        {
            KpiId = kpi.Id,
            FiscalPeriodId = march.Id,
            TargetValue = 5m,
            Granularity = TargetGranularity.Monthly
        });
        lease.Context.KpiTargets.Add(new KpiTarget
        {
            KpiId = kpi.Id,
            FiscalPeriodId = march.Id,
            TargetValue = 100m,
            Granularity = TargetGranularity.Quarterly
        });
        await lease.Context.SaveChangesAsync();

        var report = await CreateDivisionReportService(lease)
            .GetDivisionReportAsync(division.Id, scorecard.Id, year.Id);

        var marchRow = report.Single(r => r.FiscalMonth == 3);
        Assert.Equal(5m, marchRow.TargetValue);
    }
}