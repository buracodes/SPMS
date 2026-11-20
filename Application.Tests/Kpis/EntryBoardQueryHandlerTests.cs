using Application.CQRS.KpiActuals.Handlers.Queries;
using Application.CQRS.KpiActuals.Requests.Queries;
using Application.Exceptions;
using Application.Interface.IService;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

/// <summary>
/// The Data Entry board read: a division account receives only the corporate KPIs its own
/// division (or the employee personally) reports on, so another division's numbers never
/// reach the page, and a KPI of a division plan stays on the division board. A principal
/// who owns a corporate KPI personally receives it without a division claim; corporate data
/// is never a fallback, so an account with neither receives no rows.
/// </summary>
public class EntryBoardQueryHandlerTests
{
    private static readonly Guid entryUserId = Guid.NewGuid();
    private static readonly Guid otherUserId = Guid.NewGuid();

    private sealed record Fixture(
        Guid FiscalYearId,
        Guid CorporateScorecardId,
        Guid AppDevDivisionId,
        Guid TradeOpsDivisionId,
        Guid AppKpiId,
        Guid TradeKpiId,
        Guid SelfOwnedKpiId,
        Guid PeerOwnedKpiId,
        Guid PlanKpiId,
        Guid UnassignedKpiId);

    /// <summary>
    /// Builds a fiscal year with two corporate scorecards. "App Division Uptime" and "Budget
    /// Execution" belong to Application Development, "Trade Order Accuracy" to Trade
    /// Operations, "Team Onboarding Cycle" to <see cref="entryUserId"/> personally,
    /// "Peer Owned Cycle" to a colleague, "Delivery Throughput" to a division plan, and
    /// "Brand Awareness" to nobody.
    /// </summary>
    private static async Task<Fixture> CreateFixtureAsync(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var periods = calendar.GeneratePeriods(fiscalYear);

        var appDev = new Division { Id = Guid.NewGuid(), Name = "Application Development", Code = "APP" };
        var tradeOps = new Division { Id = Guid.NewGuid(), Name = "Trade Operations", Code = "TRO" };
        var corporate = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Corporate Scorecard",
        };
        var finance = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = fiscalYear.Id,
            Name = "Finance Scorecard",
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = corporate.Id, Name = "Operational Excellence", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = corporate.Id, GoalId = goal.Id, Name = "Service Delivery", Weight = 100 };

        var appKpi = NewKpi(corporate.Id, kra.Id, "App Division Uptime");
        appKpi.DivisionOwnerId = appDev.Id;
        var tradeKpi = NewKpi(corporate.Id, kra.Id, "Trade Order Accuracy");
        tradeKpi.DivisionOwnerId = tradeOps.Id;
        var selfOwnedKpi = NewKpi(corporate.Id, kra.Id, "Team Onboarding Cycle");
        selfOwnedKpi.OwnerId = entryUserId;
        var peerOwnedKpi = NewKpi(corporate.Id, kra.Id, "Peer Owned Cycle");
        peerOwnedKpi.OwnerId = otherUserId;
        var unassignedKpi = NewKpi(corporate.Id, kra.Id, "Brand Awareness");

        var financeGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = finance.Id, Name = "Financial Discipline", Weight = 100 };
        var financeKra = new Kra { Id = Guid.NewGuid(), ScorecardId = finance.Id, GoalId = financeGoal.Id, Name = "Budget Control", Weight = 100 };
        var budgetKpi = NewKpi(finance.Id, financeKra.Id, "Budget Execution");
        budgetKpi.DivisionOwnerId = appDev.Id;

        var plan = new DivisionPlan
        {
            Id = Guid.NewGuid(),
            DivisionId = appDev.Id,
            ScorecardId = corporate.Id,
            PlanStatus = DivisionPlanStatus.Finalized,
        };
        var divisionGoal = new DivisionGoal
        {
            Id = Guid.NewGuid(),
            DivisionPlanId = plan.Id,
            ParentGoalId = goal.Id,
            Name = "Division Delivery Goal",
            Weight = 100,
        };
        var planKpi = NewKpi(corporate.Id, null, "Delivery Throughput");
        planKpi.DivisionGoalId = divisionGoal.Id;
        planKpi.DivisionOwnerId = appDev.Id;

        lease.Context.FiscalYears.Add(fiscalYear);
        lease.Context.FiscalPeriods.AddRange(periods);
        lease.Context.Divisions.AddRange(appDev, tradeOps);
        lease.Context.Scorecards.AddRange(corporate, finance);
        lease.Context.Goals.AddRange(goal, financeGoal);
        lease.Context.Kras.AddRange(kra, financeKra);
        lease.Context.Users.Add(new User { Id = entryUserId, Username = "entry.user", FullName = "Entry User", DivisionId = appDev.Id });
        lease.Context.Users.Add(new User { Id = otherUserId, Username = "other.user", FullName = "Other User", DivisionId = appDev.Id });
        lease.Context.DivisionPlans.Add(plan);
        lease.Context.DivisionGoals.Add(divisionGoal);
        lease.Context.Kpis.AddRange(appKpi, tradeKpi, selfOwnedKpi, peerOwnedKpi, unassignedKpi, budgetKpi, planKpi);
        await lease.Context.SaveChangesAsync();

        return new Fixture(
            fiscalYear.Id,
            corporate.Id,
            appDev.Id,
            tradeOps.Id,
            appKpi.Id,
            tradeKpi.Id,
            selfOwnedKpi.Id,
            peerOwnedKpi.Id,
            planKpi.Id,
            unassignedKpi.Id);
    }

    private static Kpi NewKpi(Guid scorecardId, Guid? kraId, string name) => new()
    {
        Id = Guid.NewGuid(),
        ScorecardId = scorecardId,
        KraId = kraId,
        Name = name,
        Unit = "%",
        Weight = 100,
        DataSource = KpiDataSource.Manual,
        KpiType = KpiType.Lagging,
    };

    private static GetEntryBoardQueryHandler CreateHandler(
        TestDbContextLease lease,
        IOrganizationalScopeService scope)
    {
        return new GetEntryBoardQueryHandler(
            new KpiRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new GoalRepository(lease.Context),
            new KraRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            scope);
    }

    /// <summary>Application Development account with no corporate visibility â€” the data-entry persona.</summary>
    private static TestOrganizationalScopeService DivisionScope(Guid appDevDivisionId) => new()
    {
        UserId = entryUserId,
        HasCorporateAccess = false,
        DivisionIds = new[] { appDevDivisionId },
    };

    [Fact]
    public async Task DivisionAccount_ReceivesOnlyOwnDivisionAndSelfOwnedKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var scope = DivisionScope(fixture.AppDevDivisionId);

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(
            new[] { "App Division Uptime", "Budget Execution", "Team Onboarding Cycle" },
            response.Data!.Kpis.Select(k => k.Name).ToArray());

        // Another division's KPI, a colleague's personally owned KPI, an unassigned corporate
        // KPI and a division-plan KPI are absent.
        Assert.DoesNotContain(response.Data.Kpis, k => k.Id == fixture.TradeKpiId);
        Assert.DoesNotContain(response.Data.Kpis, k => k.Id == fixture.PeerOwnedKpiId);
        Assert.DoesNotContain(response.Data.Kpis, k => k.Id == fixture.UnassignedKpiId);
        Assert.DoesNotContain(response.Data.Kpis, k => k.Id == fixture.PlanKpiId);

        // Only the scorecards that actually hold a reported KPI are offered to the picker.
        Assert.Equal(
            new[] { "Corporate Scorecard", "Finance Scorecard" },
            response.Data.Scorecards.Select(s => s.Name).ToArray());
        Assert.All(response.Data.Kpis, k => Assert.Null(k.DivisionGoalId));
    }

    [Fact]
    public async Task DivisionAccount_OwningDivisionNameIsCarriedForDisplay()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var scope = DivisionScope(fixture.AppDevDivisionId);

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        var appKpi = response.Data!.Kpis.Single(k => k.Id == fixture.AppKpiId);
        Assert.Equal("Application Development", appKpi.DivisionOwnerName);
        Assert.Equal("Corporate Scorecard", appKpi.ScorecardName);
        Assert.Equal("Service Delivery", appKpi.KraName);
        Assert.Equal("Operational Excellence", appKpi.GoalName);

        var selfOwned = response.Data.Kpis.Single(k => k.Id == fixture.SelfOwnedKpiId);
        Assert.Equal("Entry User", selfOwned.OwnerName);
    }

    [Fact]
    public async Task CorporatePrincipal_ReceivesEveryCorporateKpiButNeverPlanKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var scope = new TestOrganizationalScopeService { HasCorporateAccess = true };

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "App Division Uptime",
                "Brand Awareness",
                "Budget Execution",
                "Peer Owned Cycle",
                "Team Onboarding Cycle",
                "Trade Order Accuracy",
            },
            response.Data!.Kpis.Select(k => k.Name).ToArray());
        Assert.DoesNotContain(response.Data.Kpis, k => k.Id == fixture.PlanKpiId);
    }

    [Fact]
    public async Task CorporateKpiAssignee_WithoutDivisionClaim_ReceivesOnlySelfOwnedKpi()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var scope = new TestOrganizationalScopeService
        {
            UserId = entryUserId,
            HasCorporateAccess = false,
        };

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        // Personal KPI ownership is enough to report on the corporate board; every division
        // KPI, a colleague's KPI, an unassigned KPI and a plan KPI stay out.
        var kpi = Assert.Single(response.Data!.Kpis);
        Assert.Equal(fixture.SelfOwnedKpiId, kpi.Id);
        Assert.Single(response.Data.Scorecards);
        Assert.Equal(fixture.CorporateScorecardId, response.Data.Scorecards[0].Id);
    }

    [Fact]
    public async Task AccountWithoutDivisionOrCorporateAccess_ReceivesNoBoard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var scope = new TestOrganizationalScopeService
        {
            UserId = Guid.NewGuid(),
            HasCorporateAccess = false,
        };

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        // Corporate goals, KRAs and KPIs require corporate visibility; an unassigned
        // ordinary account gets an empty board instead of a read-only corporate view.
        Assert.True(response.Success);
        Assert.Empty(response.Data!.Kpis);
        Assert.Empty(response.Data.Scorecards);
        Assert.Empty(response.Data.Targets);
        Assert.Empty(response.Data.Actuals);
    }

    [Fact]
    public async Task Board_ExcludesKpisOfOtherFiscalYears()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);

        var calendar = new FiscalCalendarService();
        var nextYear = calendar.CreateFiscalYear(2026);
        var nextScorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = nextYear.Id,
            Name = "Next Year Scorecard",
        };
        var nextGoal = new Goal { Id = Guid.NewGuid(), ScorecardId = nextScorecard.Id, Name = "Next Year Goal", Weight = 100 };
        var nextKra = new Kra { Id = Guid.NewGuid(), ScorecardId = nextScorecard.Id, GoalId = nextGoal.Id, Name = "Next Year KRA", Weight = 100 };
        var nextKpi = NewKpi(nextScorecard.Id, nextKra.Id, "Next Year App KPI");
        nextKpi.DivisionOwnerId = fixture.AppDevDivisionId;
        lease.Context.FiscalYears.Add(nextYear);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(nextYear));
        lease.Context.Scorecards.Add(nextScorecard);
        lease.Context.Goals.Add(nextGoal);
        lease.Context.Kras.Add(nextKra);
        lease.Context.Kpis.Add(nextKpi);
        await lease.Context.SaveChangesAsync();

        var scope = DivisionScope(fixture.AppDevDivisionId);

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        Assert.DoesNotContain(response.Data!.Kpis, k => k.Id == nextKpi.Id);
        Assert.DoesNotContain(response.Data.Scorecards, s => s.Id == nextScorecard.Id);
    }

    [Fact]
    public async Task Board_CarriesMonthlyAndQuarterlyTargetsWithAutoCalculateFlag()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var monthOne = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);
        var monthFour = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 4);

        lease.Context.KpiTargets.AddRange(
            new KpiTarget
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.AppKpiId,
                FiscalPeriodId = monthOne.Id,
                TargetValue = 99m,
                Granularity = TargetGranularity.Monthly,
            },
            new KpiTarget
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.AppKpiId,
                FiscalPeriodId = monthFour.Id,
                TargetValue = 297m,
                Granularity = TargetGranularity.Quarterly,
                IsDerivedFromMonthly = true,
            },
            new KpiTarget
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.TradeKpiId,
                FiscalPeriodId = monthOne.Id,
                TargetValue = 90m,
                Granularity = TargetGranularity.Monthly,
            });
        await lease.Context.SaveChangesAsync();

        var scope = DivisionScope(fixture.AppDevDivisionId);

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        var bundle = Assert.Single(response.Data!.Targets, t => t.KpiId == fixture.AppKpiId);
        Assert.Equal(fixture.FiscalYearId, bundle.FiscalYearId);
        var monthly = bundle.Targets.Single(t => t.Granularity == TargetGranularity.Monthly);
        Assert.Equal(1, monthly.FiscalMonth);
        Assert.Equal(1, monthly.Quarter);
        Assert.Equal(99m, monthly.TargetValue);
        var quarterly = bundle.Targets.Single(t => t.Granularity == TargetGranularity.Quarterly);
        Assert.Equal(4, quarterly.FiscalMonth);
        Assert.Equal(2, quarterly.Quarter);
        Assert.True(quarterly.IsDerivedFromMonthly);

        // The other division's target never rides along on a scoped KPI bundle.
        Assert.DoesNotContain(response.Data.Targets, t => t.KpiId == fixture.TradeKpiId);
    }

    [Fact]
    public async Task Board_FoldsActualsToLatestWritePerPeriodAndSource()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = await CreateFixtureAsync(lease);
        var monthOne = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 1);
        var monthTwo = await lease.Context.FiscalPeriods.SingleAsync(p => p.FiscalMonth == 2);

        lease.Context.KpiActualValues.AddRange(
            new KpiActualValue
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.AppKpiId,
                FiscalPeriodId = monthOne.Id,
                Value = 10m,
                DataSource = KpiDataSource.Manual,
                AsOfDate = new DateTime(2025, 8, 1, 8, 0, 0, DateTimeKind.Utc),
                Note = "Superseded",
            },
            new KpiActualValue
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.AppKpiId,
                FiscalPeriodId = monthOne.Id,
                Value = 12m,
                DataSource = KpiDataSource.Manual,
                AsOfDate = new DateTime(2025, 8, 3, 8, 0, 0, DateTimeKind.Utc),
                Note = "Corrected",
            },
            new KpiActualValue
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.AppKpiId,
                FiscalPeriodId = monthOne.Id,
                Value = 11m,
                DataSource = KpiDataSource.Automated,
                AsOfDate = new DateTime(2025, 8, 2, 8, 0, 0, DateTimeKind.Utc),
            },
            new KpiActualValue
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.AppKpiId,
                FiscalPeriodId = monthTwo.Id,
                Value = 20m,
                DataSource = KpiDataSource.Manual,
                AsOfDate = new DateTime(2025, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            },
            new KpiActualValue
            {
                Id = Guid.NewGuid(),
                KpiId = fixture.TradeKpiId,
                FiscalPeriodId = monthOne.Id,
                Value = 5m,
                DataSource = KpiDataSource.Manual,
                AsOfDate = new DateTime(2025, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            });
        await lease.Context.SaveChangesAsync();

        var scope = DivisionScope(fixture.AppDevDivisionId);

        var response = await CreateHandler(lease, scope)
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        var bundle = Assert.Single(response.Data!.Actuals, a => a.KpiId == fixture.AppKpiId);
        Assert.DoesNotContain(response.Data.Actuals, a => a.KpiId == fixture.TradeKpiId);

        // Month 1 keeps one manual (the latest) plus its automated feed; ordered by month, then source.
        Assert.Equal(3, bundle.Values.Count);
        Assert.Equal(new[] { 1, 1, 2 }, bundle.Values.Select(v => v.FiscalMonth).ToArray());
        var latestManual = bundle.Values.Single(v => v.FiscalMonth == 1 && v.DataSource == KpiDataSource.Manual);
        Assert.Equal(12m, latestManual.Value);
        Assert.Equal("Corrected", latestManual.Note);
        Assert.Equal(1, latestManual.Quarter);
        Assert.Equal(11m, bundle.Values.Single(v => v.DataSource == KpiDataSource.Automated).Value);
    }

    [Fact]
    public async Task Board_UnknownFiscalYear_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await CreateFixtureAsync(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler(lease, new TestOrganizationalScopeService())
                .Handle(new GetEntryBoardQueryRequest { FiscalYearId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task Board_FiscalYearWithoutScorecards_ReturnsEmptyBoard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await CreateFixtureAsync(lease);
        var emptyYear = new FiscalCalendarService().CreateFiscalYear(2030);
        lease.Context.FiscalYears.Add(emptyYear);
        lease.Context.FiscalPeriods.AddRange(new FiscalCalendarService().GeneratePeriods(emptyYear));
        await lease.Context.SaveChangesAsync();

        var response = await CreateHandler(lease, new TestOrganizationalScopeService())
            .Handle(new GetEntryBoardQueryRequest { FiscalYearId = emptyYear.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(emptyYear.Id, response.Data!.FiscalYearId);
        Assert.Empty(response.Data.Scorecards);
        Assert.Empty(response.Data.Kpis);
    }
}
