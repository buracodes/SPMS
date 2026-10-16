using Application.CQRS.Goals.Handlers.Queries;
using Application.CQRS.Goals.Requests.Queries;
using Application.Mapping;
using Application.Services;
using Application.Tests.TestSupport;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Planning;

public class GetAllGoalFilterHandlerTests
{
    private static GetAllGoalRequestHandler Handler(TestDbContextLease lease)
    {
        var mapperConfiguration = new MapperConfiguration(
            configuration => configuration.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);

        return new GetAllGoalRequestHandler(
            new GoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            mapperConfiguration.CreateMapper());
    }

    [Fact]
    public async Task GoalList_ResolvesScorecardAndFiscalYearNames()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = new FiscalCalendarService().CreateFiscalYear(2025);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Corporate 2025",
            Scope = ScorecardScope.Corporate
        };

        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow revenue", Weight = 100 });
        await lease.Context.SaveChangesAsync();

        var response = await Handler(lease).Handle(new GetAllGoalRequest(), CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("Corporate 2025", row.ScorecardName);
        Assert.Equal(year.Label, row.FiscalYearName);
    }

    [Fact]
    public async Task GoalList_FiltersByFiscalYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var calendar = new FiscalCalendarService();
        var year2025 = calendar.CreateFiscalYear(2025);
        var year2026 = calendar.CreateFiscalYear(2026);
        var scorecard2025 = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year2025.Id, Name = "Corporate 2025", Scope = ScorecardScope.Corporate };
        var scorecard2026 = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year2026.Id, Name = "Corporate 2026", Scope = ScorecardScope.Corporate };

        lease.Context.FiscalYears.AddRange(year2025, year2026);
        lease.Context.Scorecards.AddRange(scorecard2025, scorecard2026);
        lease.Context.Goals.AddRange(
            new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard2025.Id, Name = "In 2025", Weight = 100 },
            new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard2026.Id, Name = "In 2026", Weight = 100 });
        await lease.Context.SaveChangesAsync();

        var response = await Handler(lease)
            .Handle(new GetAllGoalRequest { FiscalYearId = year2026.Id }, CancellationToken.None);

        var row = Assert.Single(response.Data!);
        Assert.Equal("In 2026", row.Name);
        Assert.Equal("Corporate 2026", row.ScorecardName);
    }
}
