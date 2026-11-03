using Application.CQRS.Division.Handlers.Commands;
using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.HR;
using Application.Tests.TestSupport;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Divisions;

public class SyncDivisionsFromHrCommandHandlerTests
{
    private static readonly Guid TradingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RetiredId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static SyncDivisionsFromHrCommandHandler CreateHandler(
        TestDbContextLease lease,
        params HrDivisionDto[] divisions)
        => new(
            new TestHrInfoRepository(hrDivisions: divisions),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static HrDivisionDto HrDivision(Guid id, string name, int status = 1)
        => new() { Id = id, Name = name, Status = status };

    [Fact]
    public async Task Sync_CreatesMissingDivisionsWithHrIdsAndActiveFlag()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var handler = CreateHandler(
            lease,
            HrDivision(TradingId, "  Trading  "),
            HrDivision(RetiredId, "Retired", status: 0));

        var response = await handler.Handle(new SyncDivisionsFromHrCommandRequest(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.TotalHrDivisions);
        Assert.Equal(2, response.Data.Created);
        Assert.Equal(0, response.Data.Updated);
        Assert.Empty(response.Data.Conflicts);

        var saved = await lease.Context.Divisions.OrderBy(d => d.Name).ToListAsync();
        Assert.Equal(2, saved.Count);

        var trading = saved.Single(d => d.Name == "Trading");
        Assert.Equal(TradingId, trading.Id);
        Assert.True(trading.IsActive);
        Assert.Equal("HR-11111111", trading.Code);

        var retired = saved.Single(d => d.Name == "Retired");
        Assert.Equal(RetiredId, retired.Id);
        Assert.False(retired.IsActive);
    }

    [Fact]
    public async Task Sync_RefreshesNameAndActiveFlagForExistingHrDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        lease.Context.Divisions.Add(new Division
        {
            Id = TradingId,
            Name = "Old trading name",
            Code = "TRD",
            IsActive = false
        });
        await lease.Context.SaveChangesAsync();
        // GetAll() is no-tracking; clear the seeded instances so the update path attaches cleanly.
        lease.Context.ChangeTracker.Clear();

        var handler = CreateHandler(lease, HrDivision(TradingId, "Trading", status: 1));

        var response = await handler.Handle(new SyncDivisionsFromHrCommandRequest(), CancellationToken.None);

        Assert.Equal(0, response.Data!.Created);
        Assert.Equal(1, response.Data.Updated);

        var saved = await lease.Context.Divisions.SingleAsync();
        Assert.Equal(TradingId, saved.Id);
        Assert.Equal("Trading", saved.Name);
        Assert.Equal("TRD", saved.Code);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Sync_CountsMatchingDivisionsAsUnchanged()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        lease.Context.Divisions.Add(new Division
        {
            Id = TradingId,
            Name = "Trading",
            Code = "TRD",
            IsActive = true
        });
        await lease.Context.SaveChangesAsync();
        lease.Context.ChangeTracker.Clear();

        var handler = CreateHandler(lease, HrDivision(TradingId, "Trading"));

        var response = await handler.Handle(new SyncDivisionsFromHrCommandRequest(), CancellationToken.None);

        Assert.Equal(0, response.Data!.Created);
        Assert.Equal(0, response.Data.Updated);
        Assert.Equal(1, response.Data.Unchanged);
        Assert.Equal(1, await lease.Context.Divisions.CountAsync());
    }

    [Fact]
    public async Task Sync_SkipsHrDivisionWhoseNameBelongsToADifferentLocalId()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var manualId = Guid.NewGuid();
        lease.Context.Divisions.Add(new Division
        {
            Id = manualId,
            Name = "Finance",
            Code = "FIN",
            IsActive = true
        });
        await lease.Context.SaveChangesAsync();

        var handler = CreateHandler(lease, HrDivision(TradingId, "Finance"));

        var response = await handler.Handle(new SyncDivisionsFromHrCommandRequest(), CancellationToken.None);

        Assert.Equal(0, response.Data!.Created);
        Assert.Single(response.Data.Conflicts);
        Assert.Equal(TradingId, response.Data.Conflicts[0].HrDivisionId);
        Assert.Equal("Finance", response.Data.Conflicts[0].Name);

        var saved = await lease.Context.Divisions.SingleAsync();
        Assert.Equal(manualId, saved.Id);
        Assert.Equal("FIN", saved.Code);
    }

    [Fact]
    public async Task Sync_IgnoresBlankNamesAndDuplicateHrIds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var handler = CreateHandler(
            lease,
            HrDivision(TradingId, "Trading"),
            HrDivision(TradingId, "Trading duplicate id"),
            HrDivision(Guid.NewGuid(), "   "));

        var response = await handler.Handle(new SyncDivisionsFromHrCommandRequest(), CancellationToken.None);

        Assert.Equal(1, response.Data!.TotalHrDivisions);
        Assert.Equal(1, response.Data.Created);
        Assert.Equal(1, await lease.Context.Divisions.CountAsync());
        Assert.Equal("Trading", (await lease.Context.Divisions.SingleAsync()).Name);
    }

    [Fact]
    public async Task Sync_GeneratesACodeThatDoesNotCollideWithAnExistingManualCode()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        lease.Context.Divisions.Add(new Division
        {
            Id = Guid.NewGuid(),
            Name = "Manual division",
            Code = "HR-AAAAAAAA",
            IsActive = true
        });
        await lease.Context.SaveChangesAsync();

        var hrId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var handler = CreateHandler(lease, HrDivision(hrId, "Synced division"));

        var response = await handler.Handle(new SyncDivisionsFromHrCommandRequest(), CancellationToken.None);

        Assert.Equal(1, response.Data!.Created);
        var synced = await lease.Context.Divisions.SingleAsync(d => d.Id == hrId);
        Assert.Equal("HR-AAAAAAAA0000", synced.Code);
    }
}
