using Application.DTOS.PerformanceMonitoring;
using Application.Exceptions;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Monitoring;

/// <summary>
/// Performance monitoring is the corporate read model: it must never trust a client-supplied
/// division, must resolve corporate visibility from the live scope service, and must derive
/// its RAG counts from the existing period calculation (no separate scoring formula).
/// </summary>
public class PerformanceMonitoringServiceTests
{
    private const string Password = "P@ssw0rd!";

    private static PerformanceMonitoringService CreateService(
        TestDbContextLease lease,
        TestOrganizationalScopeService scope)
    {
        var context = lease.Context;
        var factory = new ScoreStrategyFactory();
        var clock = TestBusinessClock.Real();

        var scorePeriod = new ScorePeriodCalculationService(
            new ScorecardRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new GoalRepository(context),
            new KpiRepository(context),
            new KpiTargetRepository(context),
            new KpiActualValueRepository(context),
            new KpiScoreCalculator(factory),
            new KraScoreCalculator(),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            clock);

        var divisionPeriod = new DivisionPeriodCalculationService(
            new DivisionGoalRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new KpiTargetRepository(context),
            new KpiActualValueRepository(context),
            new KpiScoreCalculator(factory),
            new GoalScoreCalculator(),
            new ScorecardScoreCalculator(),
            clock);

        return new PerformanceMonitoringService(
            new DivisionRepository(context),
            new DivisionPlanRepository(context),
            new DivisionGoalRepository(context),
            new ScorecardRepository(context),
            new FiscalYearRepository(context),
            new FiscalPeriodRepository(context),
            new KpiRepository(context),
            new GoalRepository(context),
            new KraRepository(context),
            new UserRepository(context),
            scope,
            divisionPeriod,
            scorePeriod,
            new TestRagThresholdService());
    }

    private static async Task<FiscalYear> SeedYearAsync(TestDbContextLease lease, int calendarYear)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(calendarYear);
        lease.Context.FiscalYears.Add(year);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(year));
        await lease.Context.SaveChangesAsync();
        return year;
    }

    private static async Task<Scorecard> SeedScorecardAsync(
        TestDbContextLease lease,
        Guid fiscalYearId,
        ScorecardScope scope = ScorecardScope.Corporate,
        string name = "Corporate Scorecard")
    {
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYearId,
            Name = name,
            Scope = scope,
            ScorecardStatus = ScorecardStatus.Active
        };
        lease.Context.Scorecards.Add(scorecard);
        await lease.Context.SaveChangesAsync();
        return scorecard;
    }

    private static async Task<Division> SeedDivisionAsync(TestDbContextLease lease, string code)
    {
        var division = new Division
        {
            Id = Guid.NewGuid(),
            Name = $"{code} Division",
            Code = code,
            IsActive = true
        };
        lease.Context.Divisions.Add(division);
        await lease.Context.SaveChangesAsync();
        return division;
    }

    private static async Task<User> SeedUserAsync(TestDbContextLease lease, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@spms.et",
            Username = $"{Guid.NewGuid():N}@spms.et",
            FullName = fullName,
            PasswordHash = Password,
            IsActive = true
        };
        lease.Context.Users.Add(user);
        await lease.Context.SaveChangesAsync();
        return user;
    }

    private static async Task<(Goal Goal, Kra Kra, IReadOnlyList<Kpi> Kpis)> SeedKpiHierarchyAsync(
        TestDbContextLease lease,
        Guid scorecardId,
        Guid ownerId,
        params (string Name, decimal Weight)[] kpis)
    {
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecardId, Name = "Grow Revenue", Weight = 100 };
        var kra = new Kra
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            ScorecardId = scorecardId,
            Name = "Sales Growth",
            Weight = 100
        };

        var entities = kpis
            .Select((k, index) => new Kpi
            {
                Id = Guid.NewGuid(),
                ScorecardId = scorecardId,
                KraId = kra.Id,
                Name = k.Name,
                Weight = k.Weight,
                Unit = "%",
                IsActive = true,
                IsCumulative = false,
                OwnerId = index == 0 ? ownerId : null,
                OptimizationType = KpiOptimizationType.Maximize,
                KpiType = KpiType.Lagging
            })
            .ToList();

        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.AddRange(entities);
        await lease.Context.SaveChangesAsync();

        return (goal, kra, entities);
    }

    /// <summary>Monthly targets for every fiscal month; actuals are a share of the target when supplied.</summary>
    private static async Task SeedTargetsAsync(
        TestDbContextLease lease,
        FiscalYear year,
        Guid kpiId,
        decimal monthlyTarget,
        decimal? actualShare)
    {
        var periods = await lease.Context.FiscalPeriods
            .Where(p => p.FiscalYearId == year.Id)
            .OrderBy(p => p.FiscalMonth)
            .ToListAsync();

        foreach (var period in periods)
        {
            lease.Context.KpiTargets.Add(new KpiTarget
            {
                KpiId = kpiId,
                FiscalPeriodId = period.Id,
                Granularity = TargetGranularity.Monthly,
                TargetValue = monthlyTarget
            });

            if (actualShare.HasValue)
            {
                lease.Context.KpiActualValues.Add(new KpiActualValue
                {
                    KpiId = kpiId,
                    FiscalPeriodId = period.Id,
                    Value = monthlyTarget * actualShare.Value,
                    DataSource = KpiDataSource.Manual,
                    AsOfDate = period.EndDate
                });
            }
        }

        await lease.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Corporate_request_without_live_monitor_permission_is_forbidden()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, 2026);
        await SeedScorecardAsync(lease, year.Id);

        // The endpoint policy only needs dashboard:view; the corporate payload additionally
        // requires the live performance:monitor.view grant.
        var scope = new TestOrganizationalScopeService
        {
            UserId = Guid.NewGuid(),
            HasCorporateAccess = false
        };
        var service = CreateService(lease, scope);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetAsync(new PerformanceMonitoringFilter()));
    }

    [Fact]
    public async Task Division_principal_requesting_another_division_stays_in_own_division()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, 2026);
        await SeedScorecardAsync(lease, year.Id);
        var ownDivision = await SeedDivisionAsync(lease, "OPS");
        var otherDivision = await SeedDivisionAsync(lease, "FIN");

        var scope = new TestOrganizationalScopeService
        {
            UserId = Guid.NewGuid(),
            DivisionId = ownDivision.Id,
            HasCorporateAccess = false
        };
        var service = CreateService(lease, scope);

        // A tampered client filter must not widen the division principal's scope.
        var result = await service.GetAsync(new PerformanceMonitoringFilter { DivisionId = otherDivision.Id });

        Assert.Equal(PerformanceScope.Division, result.Scope);
        Assert.Equal(ownDivision.Id, result.DivisionId);
        Assert.Equal(ownDivision.Name, result.DivisionName);
    }

    [Fact]
    public async Task Corporate_principal_can_drill_into_any_division()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, 2026);
        await SeedScorecardAsync(lease, year.Id);
        var division = await SeedDivisionAsync(lease, "OPS");

        var scope = new TestOrganizationalScopeService
        {
            UserId = Guid.NewGuid(),
            HasCorporateAccess = true
        };
        var service = CreateService(lease, scope);

        var result = await service.GetAsync(new PerformanceMonitoringFilter { DivisionId = division.Id });

        Assert.Equal(PerformanceScope.Division, result.Scope);
        Assert.Equal(division.Id, result.DivisionId);
        Assert.Equal(0, result.KpiCount);
        Assert.Empty(result.Kpis);
    }

    [Fact]
    public async Task Scorecard_from_another_fiscal_year_is_rejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var firstYear = await SeedYearAsync(lease, 2026);
        var secondYear = await SeedYearAsync(lease, 2027);
        var scorecard = await SeedScorecardAsync(lease, firstYear.Id);

        var scope = new TestOrganizationalScopeService { HasCorporateAccess = true };
        var service = CreateService(lease, scope);

        await Assert.ThrowsAsync<BadRequestException>(() => service.GetAsync(new PerformanceMonitoringFilter
        {
            FiscalYearId = secondYear.Id,
            ScorecardId = scorecard.Id
        }));
    }

    [Fact]
    public async Task Non_corporate_scorecard_is_rejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, 2026);
        var divisionScorecard = await SeedScorecardAsync(
            lease, year.Id, ScorecardScope.Division, "Division Scorecard");

        var scope = new TestOrganizationalScopeService { HasCorporateAccess = true };
        var service = CreateService(lease, scope);

        await Assert.ThrowsAsync<BadRequestException>(() => service.GetAsync(new PerformanceMonitoringFilter
        {
            FiscalYearId = year.Id,
            ScorecardId = divisionScorecard.Id
        }));
    }

    [Fact]
    public async Task No_fiscal_year_returns_an_empty_payload_instead_of_failing()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var scope = new TestOrganizationalScopeService { HasCorporateAccess = true };
        var service = CreateService(lease, scope);

        var result = await service.GetAsync(new PerformanceMonitoringFilter());

        Assert.Null(result.FiscalYearId);
        Assert.Equal(0, result.KpiCount);
        Assert.Empty(result.Kpis);
        Assert.Empty(result.Divisions);
    }

    [Fact]
    public async Task Computes_rag_counts_and_owner_names_from_the_existing_period_calculation()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, 2026);
        var scorecard = await SeedScorecardAsync(lease, year.Id);
        var owner = await SeedUserAsync(lease, "Revenue Owner");
        var divisionOwner = await SeedDivisionAsync(lease, "OPS");

        var (_, _, kpis) = await SeedKpiHierarchyAsync(
            lease,
            scorecard.Id,
            owner.Id,
            ("On Track", 50m),
            ("At Risk", 30m),
            ("No Data", 20m));

        await SeedTargetsAsync(lease, year, kpis[0].Id, 100m, actualShare: 1.0m);  // 100 -> Green
        await SeedTargetsAsync(lease, year, kpis[1].Id, 100m, actualShare: 0.5m);  // 50 -> Red
        await SeedTargetsAsync(lease, year, kpis[2].Id, 100m, actualShare: null);  // no actual -> NoData

        // Owner type is exclusive (CK_Kpis_Owner_AtMostOne): one KPI is user-owned, another
        // division-owned, and the payload resolves each display name through its repository.
        kpis[1].DivisionOwnerId = divisionOwner.Id;
        await lease.Context.SaveChangesAsync();

        var scope = new TestOrganizationalScopeService
        {
            UserId = owner.Id,
            HasCorporateAccess = true
        };
        var service = CreateService(lease, scope);

        var result = await service.GetAsync(new PerformanceMonitoringFilter { FiscalYearId = year.Id });

        Assert.Equal(PerformanceScope.Corporate, result.Scope);
        Assert.Equal(1, result.ScorecardCount);
        Assert.Equal(1, result.GoalsCount);
        Assert.Equal(3, result.KpiCount);
        Assert.Equal(1, result.GreenCount);
        Assert.Equal(0, result.AmberCount);
        Assert.Equal(1, result.RedCount);
        Assert.Equal(1, result.NoDataCount);

        var greenRow = Assert.Single(result.Kpis, r => r.KpiName == "On Track");
        Assert.Equal("Green", greenRow.Rag);
        Assert.Equal(100m, greenRow.Score.GetValueOrDefault());
        Assert.Equal("Revenue Owner", greenRow.OwnerName);
        Assert.Null(greenRow.DivisionOwnerName);
        Assert.Equal("Grow Revenue", greenRow.GoalName);
        Assert.Equal("Sales Growth", greenRow.KraName);

        var redRow = Assert.Single(result.Kpis, r => r.KpiName == "At Risk");
        Assert.Equal("Red", redRow.Rag);
        Assert.Equal(50m, redRow.Score.GetValueOrDefault());
        Assert.Equal(divisionOwner.Name, redRow.DivisionOwnerName);
        Assert.Null(redRow.OwnerName);

        var noDataRow = Assert.Single(result.Kpis, r => r.KpiName == "No Data");
        Assert.Equal("NoData", noDataRow.Rag);
        Assert.Null(noDataRow.Score);
    }
}
