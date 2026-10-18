using Application.Dashboard;
using Application.Interface.IService;
using Domain.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Tests.TestSupport;

/// <summary>
/// In-memory IRagThresholdService used by tests: bands with the FR-607 defaults unless a test
/// overrides the values, so dashboard/factory consumers can be asserted both ways without a DB.
/// </summary>
public sealed class TestRagThresholdService : IRagThresholdService
{
    public RagThresholds Current { get; set; } = RagThresholds.Default;

    public Task<RagThresholds> GetEffectiveAsync(string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public Task<RagThresholdsConfiguration> GetConfigurationAsync(string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default)
        => Task.FromResult(new RagThresholdsConfiguration
        {
            ScopeKey = scopeKey,
            Green = Current.Green,
            Yellow = Current.Yellow,
            IsConfigured = Current.Green != RagThresholdDefaults.GreenThreshold || Current.Yellow != RagThresholdDefaults.YellowThreshold,
            Source = RagThresholdSource.Defaults
        });

    public Task<RagThresholdsConfiguration> UpdateAsync(decimal green, decimal yellow, string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("TestRagThresholdService does not persist updates; use the real RagThresholdService instead.");
}
