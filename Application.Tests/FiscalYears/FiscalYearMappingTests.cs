using Application.CQRS.FiscalYear.Handler;
using Application.DTOS.FiscalYear;
using Application.CQRS.FiscalYear.Requests;
using Application.Mapping;
using Application.Tests.TestSupport;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.FiscalYears;

/// <summary>
/// Regression tests for the fiscal-year list projection. The list endpoint is the only
/// consumer of <see cref="FiscalYearDto"/> through AutoMapper; an unqualified
/// <c>CreateMap</c> bound <see cref="CommonEntity.Status"/> (always
/// <see cref="EntityStatus.Active"/>, the soft-delete row state) to
/// <see cref="FiscalYearDto.Status"/> instead of the lifecycle status, so every year was
/// reported as "Active" and the UI's FR-703 edit gating rejected all edits.
/// </summary>
public class FiscalYearMappingTests
{
    [Fact]
    public void Map_List_DtoStatusReflectsFiscalYearStatus()
    {
        var mapper = CreateMapper();

        var open = mapper.Map<FiscalYearDto>(MakeYear(FiscalYearStatus.Open));
        var closed = mapper.Map<FiscalYearDto>(MakeYear(FiscalYearStatus.Closed));
        var archived = mapper.Map<FiscalYearDto>(MakeYear(FiscalYearStatus.Archived));

        Assert.Equal("Open", open.Status);
        Assert.Equal("Closed", closed.Status);
        Assert.Equal("Archived", archived.Status);
    }

    [Fact]
    public void Map_List_ReproductionScenario_ReportsOpenYearAsEditable()
    {
        // Reproduction: July 1, 2026 – June 30, 2027, current date October 16, 2026, no
        // scorecards. The year is Open and has not ended, so the frontend must see status
        // "Open" (not the soft-delete "Active") and keep editing available.
        var year = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "FY2026/27",
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2027, 6, 30),
            FiscalYearStatus = FiscalYearStatus.Open,
        };

        var dto = CreateMapper().Map<FiscalYearDto>(year);

        Assert.Equal("Open", dto.Status);
        Assert.NotEqual(EntityStatus.Active.ToString(), dto.Status);
        Assert.False(dto.HasScorecards);
    }

    [Fact]
    public async Task Handler_List_PreservesLifecycleStatusAndScorecardFlag()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "FY2026/27",
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2027, 6, 30),
            FiscalYearStatus = FiscalYearStatus.Open,
        };
        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.Add(new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Corporate 2026",
            Scope = ScorecardScope.Corporate,
        });
        await lease.Context.SaveChangesAsync();

        var mapper = CreateMapper();
        var handler = new GetFiscalYearHandler(
            new FiscalYearRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            mapper);
        var response = await handler.Handle(new GetFiscalYearRequest(), CancellationToken.None);

        var dto = Assert.Single(response.Data);
        Assert.Equal("Open", dto.Status);
        Assert.True(dto.HasScorecards);
    }

    private static FiscalYear MakeYear(FiscalYearStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Label = $"FY {status}",
        StartDate = new DateTime(2026, 7, 1),
        EndDate = new DateTime(2027, 6, 30),
        FiscalYearStatus = status,
    };

    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        return configuration.CreateMapper();
    }
}
