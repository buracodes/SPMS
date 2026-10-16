using Application.CQRS.Organization.Handlers;
using Application.CQRS.Organization.Requests;
using Application.DTOS.Organization;
using Application.Mapping;
using Application.Tests.TestSupport;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Organization;

public class OrganizationProfileTests
{
    private readonly IMapper _mapper;

    public OrganizationProfileTests()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<OrganizationMappingProfile>(),
            NullLoggerFactory.Instance);
        _mapper = config.CreateMapper();
    }

    [Fact]
    public async Task GetOrganizationProfile_ReturnsFullSeededData()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var profile = new OrganizationProfile
        {
            Id = Guid.NewGuid(),
            Name = "Ethiopian Commodity Exchange",
            Vision = "World class exchange",
            Mission = "Transparent trading platform",
            CoreValues = new List<CoreValue>
            {
                new CoreValue { Id = Guid.NewGuid(), Title = "Integrity" }
            },
            StrategicThemes = new List<StrategicTheme>
            {
                new StrategicTheme { Id = Guid.NewGuid(), Title = "Digital Transformation", Code = "ST-1" }
            },
            SwotItems = new List<SwotItem>
            {
                new SwotItem { Id = Guid.NewGuid(), Category = SwotCategory.Strength, Title = "Warehouse Network", Impact = ImpactLevel.High }
            },
            PestelItems = new List<PestelItem>
            {
                new PestelItem { Id = Guid.NewGuid(), Category = PestelCategory.Political, Title = "Gov Policy Support", Impact = ImpactLevel.High }
            }
        };

        lease.Context.OrganizationProfiles.Add(profile);
        await lease.Context.SaveChangesAsync();

        var repo = new OrganizationProfileRepository(lease.Context);
        var handler = new GetOrganizationProfileRequestHandler(repo, _mapper);

        var result = await handler.Handle(new GetOrganizationProfileRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Ethiopian Commodity Exchange", result.Data.Name);
        Assert.Single(result.Data.CoreValues);
        Assert.Single(result.Data.StrategicThemes);
        Assert.Single(result.Data.SwotItems);
        Assert.Single(result.Data.PestelItems);
    }

    [Fact]
    public async Task CreateSwotItem_AddsNewSwotItemToProfile()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var profile = new OrganizationProfile
        {
            Id = Guid.NewGuid(),
            Name = "ECX",
            Vision = "Vision",
            Mission = "Mission"
        };
        lease.Context.OrganizationProfiles.Add(profile);
        await lease.Context.SaveChangesAsync();

        var repo = new OrganizationProfileRepository(lease.Context);
        var swotRepo = new GenericRepository<SwotItem>(lease.Context);
        var handler = new CreateSwotItemCommandHandler(repo, swotRepo, _mapper);

        var command = new CreateSwotItemCommand
        {
            Dto = new CreateSwotItemDto
            {
                Category = SwotCategory.Opportunity,
                Title = "Traceability Certification",
                Description = "Expand specialty coffee market",
                Impact = ImpactLevel.High
            }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Traceability Certification", result.Data.Title);
        Assert.Equal(SwotCategory.Opportunity, result.Data.Category);

        var count = await lease.Context.SwotItems.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreatePestelItem_AddsNewPestelItemToProfile()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var profile = new OrganizationProfile
        {
            Id = Guid.NewGuid(),
            Name = "ECX",
            Vision = "Vision",
            Mission = "Mission"
        };
        lease.Context.OrganizationProfiles.Add(profile);
        await lease.Context.SaveChangesAsync();

        var repo = new OrganizationProfileRepository(lease.Context);
        var pestelRepo = new GenericRepository<PestelItem>(lease.Context);
        var handler = new CreatePestelItemCommandHandler(repo, pestelRepo, _mapper);

        var command = new CreatePestelItemCommand
        {
            Dto = new CreatePestelItemDto
            {
                Category = PestelCategory.Technological,
                Title = "Mobile Price Ticker",
                Description = "SMS alerts to farmers",
                Impact = ImpactLevel.High
            }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Mobile Price Ticker", result.Data.Title);
        Assert.Equal(PestelCategory.Technological, result.Data.Category);

        var count = await lease.Context.PestelItems.CountAsync();
        Assert.Equal(1, count);
    }
}
