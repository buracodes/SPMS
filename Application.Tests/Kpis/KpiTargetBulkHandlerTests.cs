using Application.Behaviors;
using Application.CQRS.KpiTargets.Handlers.Commands;
using Application.CQRS.KpiTargets.Handlers.Queries;
using Application.CQRS.KpiTargets.Requests.Commands;
using Application.CQRS.KpiTargets.Requests.Queries;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Application.Services;
using Application.Tests.TestSupport;
using Application.Validation.KpiTarget;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using Infrastructure.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Tests.Kpis;

public class KpiTargetBulkHandlerTests
{
    private sealed record Fixture(
        Guid FiscalYearId,
        Guid OtherFiscalYearId,
        Guid ScorecardId,
        Guid OtherScorecardId,
        Guid StaleScorecardId,
        Guid GoalId,
        Guid KraId,
        Guid FirstKpiId,
        Guid SecondKpiId,
        Guid DivisionKpiId,
        Guid OtherYearKpiId);

    private static Fixture CreateFixture(TestDbContextLease lease)
    {
        var calendar = new FiscalCalendarService();
        var fiscalYear = calendar.CreateFiscalYear(2025);
        var otherFiscalYear = calendar.CreateFiscalYear(2026);

        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Corporate", Scope = ScorecardScope.Corporate
        };
        var otherScorecard = new Scorecard
        {
            Id = Guid.NewGuid(), FiscalYearId = fiscalYear.Id, Name = "Division", Scope = ScorecardScope.Division
        };
        var staleScorecard = new Scorecard
        {
            Id = Guid.NewGuid(), FiscalYearId = otherFiscalYear.Id, Name = "Last Year", Scope = ScorecardScope.Corporate
        };

        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow volume", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Win mandates", Weight = 100 };

        var firstKpi = NewKpi(scorecard.Id, kra.Id, "Revenue growth");
        var secondKpi = NewKpi(scorecard.Id, kra.Id, "Margin growth");
        var divisionKpi = NewKpi(otherScorecard.Id, null, "Division KPI");
        var otherYearKpi = NewKpi(staleScorecard.Id, null, "Last year KPI");

        lease.Context.FiscalYears.AddRange(fiscalYear, otherFiscalYear);
        lease.Context.FiscalPeriods.AddRange(calendar.GeneratePeriods(fiscalYear));
        lease.Context.Scorecards.AddRange(scorecard, otherScorecard, staleScorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.AddRange(firstKpi, secondKpi, divisionKpi, otherYearKpi);
        lease.Context.SaveChanges();

        return new Fixture(
            fiscalYear.Id, otherFiscalYear.Id, scorecard.Id, otherScorecard.Id, staleScorecard.Id,
            goal.Id, kra.Id, firstKpi.Id, secondKpi.Id, divisionKpi.Id, otherYearKpi.Id);
    }

    private static Kpi NewKpi(Guid scorecardId, Guid? kraId, string name)
    {
        return new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecardId,
            KraId = kraId,
            Name = name,
            Weight = 50,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
    }

    private static GetKpiTargetsBulkQueryHandler CreateQueryHandler(TestDbContextLease lease)
    {
        return new GetKpiTargetsBulkQueryHandler(
            new KpiRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalPeriodRepository(lease.Context),
            new KpiTargetRepository(lease.Context));
    }

    /// <summary>Real MediatR pipeline, so the bulk handler exercises the same path as production.</summary>
    private static IMediator CreateMediator(TestDbContextLease lease)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddValidatorsFromAssembly(typeof(SetKpiTargetsCommandRequest).Assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(SetKpiTargetsCommandRequest).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddScoped<IKpiRepository>(_ => new KpiRepository(lease.Context));
        services.AddScoped<IFiscalYearRepository>(_ => new FiscalYearRepository(lease.Context));
        services.AddScoped<IFiscalPeriodRepository>(_ => new FiscalPeriodRepository(lease.Context));
        services.AddScoped<IKpiTargetRepository>(_ => new KpiTargetRepository(lease.Context));
        services.AddSingleton<IFiscalCalendarService, FiscalCalendarService>();
        services.AddScoped<ISpmsJobScheduler, TestSpmsJobScheduler>();

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    private static SetKpiTargetsItemDto Item(Guid kpiId, decimal annual = 100)
    {
        return new SetKpiTargetsItemDto
        {
            KpiId = kpiId,
            MonthlyTargets = Enumerable.Range(1, 12).Select(m => (decimal)m).ToList(),
            QuarterlyTargets = Enumerable.Range(1, 4).Select(q => (decimal)q * 3).ToList(),
            AnnualTarget = annual
        };
    }

    // ── Bulk read ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetKpiTargetsBulk_ReturnsEveryKpiOfTheFiscalYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var response = await CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(fixture.FiscalYearId, response.Data!.FiscalYearId);
        Assert.Null(response.Data.ScorecardId);

        // Two KPIs on the 2025 corporate scorecard plus the one on its Division scorecard;
        // the KPI that belongs to the 2026 fiscal year must not appear.
        Assert.Equal(3, response.Data.Kpis.Count);
        Assert.DoesNotContain(response.Data.Kpis, b => b.KpiId == fixture.OtherYearKpiId);
        Assert.Contains(response.Data.Kpis, b => b.KpiId == fixture.DivisionKpiId);
        Assert.All(response.Data.Kpis, b => Assert.Equal(fixture.FiscalYearId, b.FiscalYearId));
    }

    [Fact]
    public async Task GetKpiTargetsBulk_GroupsSavedTargetsUnderTheirKpi()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var bulkCommand = new SetKpiTargetsBulkCommandHandler(CreateMediator(lease));
        await bulkCommand.Handle(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto
            {
                FiscalYearId = fixture.FiscalYearId,
                Targets = new List<SetKpiTargetsItemDto> { Item(fixture.FirstKpiId, 250) }
            }
        }, CancellationToken.None);

        var response = await CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest { FiscalYearId = fixture.FiscalYearId }, CancellationToken.None);

        var bundle = Assert.Single(response.Data!.Kpis, b => b.KpiId == fixture.FirstKpiId);
        Assert.Equal(17, bundle.Targets.Count);

        var annual = Assert.Single(bundle.Targets, t => t.Granularity == TargetGranularity.Annual);
        Assert.Equal(250, annual.TargetValue);
        Assert.Equal(12, annual.FiscalMonth);

        var q1 = Assert.Single(bundle.Targets, t => t.Granularity == TargetGranularity.Quarterly && t.TargetValue == 3);
        Assert.Equal(1, q1.Quarter);
        Assert.Equal(3, q1.FiscalMonth);

        // The KPI that was never saved still comes back, just with nothing in it.
        var untouched = Assert.Single(response.Data.Kpis, b => b.KpiId == fixture.SecondKpiId);
        Assert.Empty(untouched.Targets);
    }

    [Fact]
    public async Task GetKpiTargetsBulk_NarrowsToASingleScorecard()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var response = await CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest
            {
                FiscalYearId = fixture.FiscalYearId,
                ScorecardId = fixture.ScorecardId
            }, CancellationToken.None);

        Assert.Equal(fixture.ScorecardId, response.Data!.ScorecardId);
        Assert.Equal(2, response.Data.Kpis.Count);
        Assert.All(response.Data.Kpis, b => Assert.Contains(b.KpiId, new[] { fixture.FirstKpiId, fixture.SecondKpiId }));
    }

    [Fact]
    public async Task GetKpiTargetsBulk_ScorecardOfAnotherFiscalYear_ReturnsNothing()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        // The scorecard exists, but it belongs to the 2026 fiscal year, so it is outside
        // the requested scope rather than an error.
        var response = await CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest
            {
                FiscalYearId = fixture.FiscalYearId,
                ScorecardId = fixture.StaleScorecardId
            }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Data!.Kpis);
    }

    [Fact]
    public async Task GetKpiTargetsBulk_DivisionScorecard_ReturnsOnlyItsOwnKpis()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var response = await CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest
            {
                FiscalYearId = fixture.FiscalYearId,
                ScorecardId = fixture.OtherScorecardId
            }, CancellationToken.None);

        Assert.Equal(fixture.OtherScorecardId, response.Data!.ScorecardId);
        Assert.Equal(fixture.DivisionKpiId, Assert.Single(response.Data.Kpis).KpiId);
    }

    [Fact]
    public async Task GetKpiTargetsBulk_UnknownFiscalYear_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        CreateFixture(lease);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest { FiscalYearId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task GetKpiTargetsBulk_UnknownScorecard_ThrowsNotFound()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateQueryHandler(lease).Handle(
            new GetKpiTargetsBulkQueryRequest
            {
                FiscalYearId = fixture.FiscalYearId,
                ScorecardId = Guid.NewGuid()
            }, CancellationToken.None));
    }

    // ── Bulk write ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetKpiTargetsBulk_SavesEveryKpiInOneRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var handler = new SetKpiTargetsBulkCommandHandler(CreateMediator(lease));
        var response = await handler.Handle(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto
            {
                FiscalYearId = fixture.FiscalYearId,
                Targets = new List<SetKpiTargetsItemDto>
                {
                    Item(fixture.FirstKpiId, 100),
                    Item(fixture.SecondKpiId, 200)
                }
            }
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.SavedCount);
        Assert.Equal(0, response.Data.FailedCount);
        Assert.All(response.Data.Results, r => Assert.True(r.Success));

        var saved = await lease.Context.KpiTargets
            .Where(t => t.KpiId == fixture.FirstKpiId || t.KpiId == fixture.SecondKpiId)
            .ToListAsync();

        Assert.Equal(34, saved.Count);
        Assert.Equal(200, saved
            .Single(t => t.KpiId == fixture.SecondKpiId && t.Granularity == TargetGranularity.Annual)
            .TargetValue);
    }

    [Fact]
    public async Task SetKpiTargetsBulk_OneInvalidKpi_ReportsThatRow_AndStillSavesTheRest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        // A KPI that does not exist fails on its own row instead of rejecting the batch.
        var handler = new SetKpiTargetsBulkCommandHandler(CreateMediator(lease));
        var response = await handler.Handle(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto
            {
                FiscalYearId = fixture.FiscalYearId,
                Targets = new List<SetKpiTargetsItemDto>
                {
                    Item(fixture.FirstKpiId),
                    Item(Guid.NewGuid())
                }
            }
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(1, response.Data!.SavedCount);
        Assert.Equal(1, response.Data.FailedCount);
        Assert.Contains("saved, 1 failed", response.Message);

        var failed = Assert.Single(response.Data.Results, r => !r.Success);
        Assert.False(string.IsNullOrWhiteSpace(failed.Message));

        Assert.Equal(17, await lease.Context.KpiTargets.CountAsync(t => t.KpiId == fixture.FirstKpiId));
    }

    [Fact]
    public async Task SetKpiTargetsBulk_InvalidMonthlyCount_FailsOnlyThatRow()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var broken = Item(fixture.SecondKpiId);
        broken.MonthlyTargets = new List<decimal> { 1, 2, 3 };

        var handler = new SetKpiTargetsBulkCommandHandler(CreateMediator(lease));
        var response = await handler.Handle(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto
            {
                FiscalYearId = fixture.FiscalYearId,
                Targets = new List<SetKpiTargetsItemDto> { Item(fixture.FirstKpiId), broken }
            }
        }, CancellationToken.None);

        Assert.Equal(1, response.Data!.SavedCount);
        var failed = Assert.Single(response.Data.Results, r => !r.Success);
        Assert.Contains("12 months", failed.Message, StringComparison.OrdinalIgnoreCase);

        // The invalid row must not have been written at all.
        Assert.Empty(await lease.Context.KpiTargets.Where(t => t.KpiId == fixture.SecondKpiId).ToListAsync());
    }

    [Fact]
    public async Task SetKpiTargetsBulk_EmptyTargetList_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var handler = new SetKpiTargetsBulkCommandHandler(CreateMediator(lease));

        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto { FiscalYearId = fixture.FiscalYearId }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task SetKpiTargetsBulk_Rewrite_ReplacesRatherThanDuplicates()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var fixture = CreateFixture(lease);

        var handler = new SetKpiTargetsBulkCommandHandler(CreateMediator(lease));
        var request = new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto
            {
                FiscalYearId = fixture.FiscalYearId,
                Targets = new List<SetKpiTargetsItemDto> { Item(fixture.FirstKpiId, 100) }
            }
        };

        await handler.Handle(request, CancellationToken.None);
        request.Dto.Targets = new List<SetKpiTargetsItemDto> { Item(fixture.FirstKpiId, 400) };
        await handler.Handle(request, CancellationToken.None);

        var targets = await lease.Context.KpiTargets.Where(t => t.KpiId == fixture.FirstKpiId).ToListAsync();
        Assert.Equal(17, targets.Count);
        Assert.Equal(400, targets.Single(t => t.Granularity == TargetGranularity.Annual).TargetValue);
    }

    // ── Validator ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetKpiTargetsBulkValidator_RequiresFiscalYearAndAtLeastOneKpi()
    {
        var validator = new SetKpiTargetsBulkValidator();

        var missingYear = validator.Validate(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto { Targets = new List<SetKpiTargetsItemDto> { Item(Guid.NewGuid()) } }
        });
        Assert.False(missingYear.IsValid);
        Assert.Contains(missingYear.Errors, e => e.ErrorMessage.Contains("Fiscal year", StringComparison.OrdinalIgnoreCase));

        var emptyList = validator.Validate(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto { FiscalYearId = Guid.NewGuid() }
        });
        Assert.False(emptyList.IsValid);
        Assert.Contains(emptyList.Errors, e => e.ErrorMessage.Contains("At least one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetKpiTargetsBulkValidator_DoesNotRejectTheBatchForOneBadRow()
    {
        // Per-KPI rules belong to the single-KPI command, so the batch must survive a
        // malformed item and let the handler report it against that row.
        var validator = new SetKpiTargetsBulkValidator();
        var broken = Item(Guid.NewGuid());
        broken.MonthlyTargets = new List<decimal> { 1 };

        var result = validator.Validate(new SetKpiTargetsBulkCommandRequest
        {
            Dto = new SetKpiTargetsBulkDto
            {
                FiscalYearId = Guid.NewGuid(),
                Targets = new List<SetKpiTargetsItemDto> { broken }
            }
        });

        Assert.True(result.IsValid);
    }
}
