using Application.Dashboard;
using Application.Exceptions;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Application.Tests.Dashboard;

/// <summary>
/// FR-607 configurable RAG thresholds: defaults when nothing is stored, persistence of all four
/// band rows on update, validation guarding the band rules, and fallback + warnings when stored
/// configuration is incomplete or unusable. These guarantees are what lets the dashboard, reports,
/// division planning and scorecard views band the same score identically.
/// </summary>
public class RagThresholdServiceTests
{
    private static RagThresholdService Create(TestDbContextLease lease)
        => new(
            new RagThresholdConfigRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static RagThresholdConfig Row(RagBand band, decimal? min, decimal? max, string color) => new()
    {
        Id = Guid.NewGuid(),
        ScopeKey = RagThresholdDefaults.ScopeKey,
        Band = band,
        MinScore = min,
        MaxScore = max,
        Color = color,
        Status = EntityStatus.Active,
        IsDeleted = false,
        CreatedDate = DateTime.UtcNow
    };

    private static async Task SeedRowsAsync(TestDbContextLease lease, params RagThresholdConfig[] rows)
    {
        lease.Context.RagThresholdConfigs.AddRange(rows);
        await lease.Context.SaveChangesAsync();
        lease.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task GetEffective_WithNoStoredRows_UsesTheFrdDefaults()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        var config = await service.GetConfigurationAsync();

        Assert.Equal(RagThresholdDefaults.GreenThreshold, config.Green);
        Assert.Equal(RagThresholdDefaults.YellowThreshold, config.Yellow);
        Assert.False(config.IsConfigured);
        Assert.Equal(RagThresholdSource.Defaults, config.Source);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public async Task Update_PersistsAllFourBandsAndBandsScoresWithTheNewValues()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        var config = await service.UpdateAsync(green: 90m, yellow: 60m);

        // The update itself is effective immediately.
        Assert.Equal(RagThresholdSource.Database, config.Source);
        Assert.Equal(90m, config.Green);
        Assert.Equal(60m, config.Yellow);

        lease.Context.ChangeTracker.Clear();
        var rows = await lease.Context.RagThresholdConfigs
            .Where(r => r.ScopeKey == RagThresholdDefaults.ScopeKey)
            .ToListAsync();
        Assert.Equal(4, rows.Count);

        var green = rows.Single(r => r.Band == RagBand.Green);
        var amber = rows.Single(r => r.Band == RagBand.Yellow);
        var red = rows.Single(r => r.Band == RagBand.Red);
        var gray = rows.Single(r => r.Band == RagBand.Gray);
        Assert.Equal(90m, green.MinScore);
        Assert.Null(green.MaxScore);
        Assert.Equal(60m, amber.MinScore);
        Assert.Equal(90m, amber.MaxScore);
        Assert.Equal(60m, red.MaxScore);
        Assert.Null(red.MinScore);
        Assert.NotEmpty(gray.Color);

        // New bands applied: 95 was Green under defaults, now it sits in the Amber band.
        Assert.Equal(DashboardRag.Green, config.Thresholds.Classify(95m));
        Assert.Equal(DashboardRag.Green, config.Thresholds.Classify(90m));
        Assert.Equal(DashboardRag.Amber, config.Thresholds.Classify(89.9m));
        Assert.Equal(DashboardRag.Amber, config.Thresholds.Classify(60m));
        Assert.Equal(DashboardRag.Red, config.Thresholds.Classify(59.9m));
    }

    [Fact]
    public async Task Update_ReplacesPreviouslyStoredBands()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        await service.UpdateAsync(green: 90m, yellow: 60m);
        var config = await service.UpdateAsync(green: 98m, yellow: 70m);

        Assert.Equal(98m, config.Green);
        Assert.Equal(70m, config.Yellow);

        lease.Context.ChangeTracker.Clear();
        var rows = await lease.Context.RagThresholdConfigs
            .Where(r => r.ScopeKey == RagThresholdDefaults.ScopeKey)
            .ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal(98m, rows.Single(r => r.Band == RagBand.Green).MinScore);
        Assert.Equal(70m, rows.Single(r => r.Band == RagBand.Yellow).MinScore);
    }

    [Fact]
    public async Task Update_RejectsYellowAtOrAboveGreen_AndPersistsNothing()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        var exception = await Assert.ThrowsAsync<ValidationExceptions>(
            () => service.UpdateAsync(green: 60m, yellow: 60m));

        Assert.Contains(
            exception.Errors,
            e => e.ErrorMessage.Contains("strictly lower", StringComparison.Ordinal));

        lease.Context.ChangeTracker.Clear();
        Assert.Empty(await lease.Context.RagThresholdConfigs.ToListAsync());

        // Defaults still govern the next read.
        var config = await service.GetConfigurationAsync();
        Assert.Equal(RagThresholdSource.Defaults, config.Source);
    }

    [Fact]
    public async Task Update_RejectsOutOfRangeThresholds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        var tooHigh = await Assert.ThrowsAsync<ValidationExceptions>(
            () => service.UpdateAsync(green: 150m, yellow: 60m));
        Assert.Contains(tooHigh.Errors, e => e.Field == "green");

        var negative = await Assert.ThrowsAsync<ValidationExceptions>(
            () => service.UpdateAsync(green: 95m, yellow: -5m));
        Assert.Contains(negative.Errors, e => e.Field == "yellow");
    }

    [Fact]
    public async Task GetConfiguration_FallsBackToDefaultsWithWarningsWhenStoredRowsAreIncomplete()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedRowsAsync(lease, Row(RagBand.Yellow, RagThresholdDefaults.YellowThreshold, RagThresholdDefaults.GreenThreshold, RagThresholdDefaults.YellowColor));
        var service = Create(lease);

        var config = await service.GetConfigurationAsync();

        // Rows exist, so the configuration is reported as configured even though only the Amber
        // band is usable - the missing Green band falls back to the FR-607 default with a warning.
        Assert.Equal(RagThresholdSource.Defaults, config.Source);
        Assert.True(config.IsConfigured);
        Assert.Contains(config.Warnings, w => w.Contains("Green band configuration is missing", StringComparison.Ordinal));
        Assert.Equal(RagThresholdDefaults.GreenThreshold, config.Green);
        Assert.Equal(RagThresholdDefaults.YellowThreshold, config.Yellow);
    }

    [Fact]
    public async Task GetConfiguration_FallsBackToDefaultsWhenTheStoredPairIsInvalid()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedRowsAsync(
            lease,
            Row(RagBand.Green, 70m, null, RagThresholdDefaults.GreenColor),
            Row(RagBand.Yellow, 80m, 70m, RagThresholdDefaults.YellowColor),
            Row(RagBand.Red, null, 80m, RagThresholdDefaults.RedColor),
            Row(RagBand.Gray, null, null, RagThresholdDefaults.GrayColor));
        var service = Create(lease);

        var config = await service.GetConfigurationAsync();

        Assert.Equal(RagThresholdSource.Defaults, config.Source);
        Assert.Contains(config.Warnings, w => w.Contains("Stored RAG thresholds are invalid", StringComparison.Ordinal));
        Assert.Equal(RagThresholdDefaults.GreenThreshold, config.Green);
        Assert.Equal(RagThresholdDefaults.YellowThreshold, config.Yellow);
    }

    [Fact]
    public async Task MemoizationLoadsTheConfigurationOncePerServiceInstance()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        var first = await service.GetConfigurationAsync();
        var second = await service.GetConfigurationAsync();
        Assert.Same(first, second);

        // Mutating stored rows behind the service's back does not refresh a cached scope, so one
        // logical operation bands every score with one set of values.
        await SeedRowsAsync(lease, Row(RagBand.Green, 50m, null, RagThresholdDefaults.GreenColor));
        var third = await service.GetConfigurationAsync();
        Assert.Same(first, third);
    }

    [Fact]
    public async Task UpdateThenReadAcrossServiceInstances_SeesThePersistedConfiguration()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);

        await service.UpdateAsync(green: 85m, yellow: 40m);

        var secondReader = new RagThresholdService(
            new RagThresholdConfigRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
        var config = await secondReader.GetConfigurationAsync();

        Assert.Equal(RagThresholdSource.Database, config.Source);
        Assert.Equal(85m, config.Green);
        Assert.Equal(40m, config.Yellow);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public async Task ScoreBanding_EvaluatesThroughThePersistedConfiguration()
    {
        // The gauge/plan/report evaluator is the same RagThresholds instance built from stored
        // rows, not a parallel constant set: the same score must band identically everywhere.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = Create(lease);
        var config = await service.UpdateAsync(green: 85m, yellow: 40m);

        Assert.Equal(DashboardRag.Amber, config.Thresholds.Classify(80m));
        Assert.Equal(DashboardRag.Green, config.Thresholds.Classify(85m));
        Assert.Equal(DashboardRag.NoData, config.Thresholds.Classify(null));
        Assert.True(config.Thresholds.IsRed(39m));
        Assert.False(config.Thresholds.IsRed(41m));
    }
}
