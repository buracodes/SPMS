using Application.Dashboard;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services
{
    /// <summary>
    /// Loads the effective RAG thresholds (FR-607) from <c>RagThresholdConfig</c> and upserts them
    /// through the authorized API. The service is scoped, so the first load in a request is memoized
    /// and every consumer in that request bands scores with the same values.
    /// </summary>
    public sealed class RagThresholdService : IRagThresholdService
    {
        private readonly IRagThresholdConfigRepository repository;
        private readonly IUnitOfWork unitOfWork;
        private readonly Dictionary<string, RagThresholdsConfiguration> loaded = new();

        public RagThresholdService(IRagThresholdConfigRepository _repository, IUnitOfWork _unitOfWork)
        {
            repository = _repository;
            unitOfWork = _unitOfWork;
        }

        public async Task<RagThresholds> GetEffectiveAsync(string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default)
        {
            return (await GetConfigurationAsync(scopeKey, cancellationToken)).Thresholds;
        }

        public async Task<RagThresholdsConfiguration> GetConfigurationAsync(string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default)
        {
            if (loaded.TryGetValue(scopeKey, out var cached))
            {
                return cached;
            }

            var configuration = await LoadAsync(scopeKey, cancellationToken);
            loaded[scopeKey] = configuration;
            return configuration;
        }

        public async Task<RagThresholdsConfiguration> UpdateAsync(decimal green, decimal yellow, string scopeKey = RagThresholdDefaults.ScopeKey, CancellationToken cancellationToken = default)
        {
            var errors = RagThresholds.Validate(green, yellow);
            if (errors.Count != 0)
            {
                throw new ValidationExceptions(errors.Select(message => new ValidationError
                {
                    PropertyName = FieldFor(message),
                    Field = FieldFor(message),
                    ErrorMessage = message
                }));
            }

            var rows = await repository.GetByScopeAsync(scopeKey, cancellationToken);
            var byBand = rows.ToDictionary(x => x.Band);

            foreach (var band in new[] { RagBand.Green, RagBand.Yellow, RagBand.Red, RagBand.Gray })
            {
                var minScore = MinScoreFor(band, green, yellow);
                var maxScore = MaxScoreFor(band, green, yellow);
                var color = ColorFor(band);

                if (byBand.TryGetValue(band, out var row))
                {
                    row.MinScore = minScore;
                    row.MaxScore = maxScore;
                    if (string.IsNullOrWhiteSpace(row.Color))
                    {
                        row.Color = color;
                    }

                    await repository.UpdateWithoutSave(row);
                }
                else
                {
                    await repository.AddWithoutSave(new RagThresholdConfig
                    {
                        Id = Guid.NewGuid(),
                        ScopeKey = scopeKey,
                        Band = band,
                        MinScore = minScore,
                        MaxScore = maxScore,
                        Color = color,
                        Status = EntityStatus.Active,
                        IsDeleted = false
                    });
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            loaded.Remove(scopeKey);
            return await GetConfigurationAsync(scopeKey, cancellationToken);
        }

        private async Task<RagThresholdsConfiguration> LoadAsync(string scopeKey, CancellationToken cancellationToken)
        {
            var rows = await repository.GetByScopeAsync(scopeKey, cancellationToken);
            var warnings = new List<string>();

            var greenRow = rows.FirstOrDefault(x => x.Band == RagBand.Green);
            var yellowRow = rows.FirstOrDefault(x => x.Band == RagBand.Yellow);
            var configured = greenRow is not null || yellowRow is not null;

            var green = greenRow?.MinScore ?? RagThresholdDefaults.GreenThreshold;
            var yellow = yellowRow?.MinScore ?? RagThresholdDefaults.YellowThreshold;

            var inEffectFromDatabase = configured;
            if (configured)
            {
                if (greenRow is null || greenRow.MinScore is null)
                {
                    warnings.Add($"Green band configuration is missing; the FR-607 default of {RagThresholdDefaults.GreenThreshold}% is in effect.");
                    green = RagThresholdDefaults.GreenThreshold;
                    inEffectFromDatabase = false;
                }

                if (yellowRow is null || yellowRow.MinScore is null)
                {
                    warnings.Add($"Yellow band configuration is missing; the FR-607 default of {RagThresholdDefaults.YellowThreshold}% is in effect.");
                    yellow = RagThresholdDefaults.YellowThreshold;
                    inEffectFromDatabase = false;
                }

                if (RagThresholds.TryCreate(green, yellow) is null)
                {
                    warnings.Add($"Stored RAG thresholds are invalid (Yellow {yellow} must be strictly lower than Green {green}); FR-607 defaults are in effect.");
                    green = RagThresholdDefaults.GreenThreshold;
                    yellow = RagThresholdDefaults.YellowThreshold;
                    inEffectFromDatabase = false;
                }
            }

            return new RagThresholdsConfiguration
            {
                ScopeKey = scopeKey,
                Green = green,
                Yellow = yellow,
                IsConfigured = configured,
                Source = inEffectFromDatabase ? RagThresholdSource.Database : RagThresholdSource.Defaults,
                GreenColor = ColorOf(RagBand.Green, greenRow),
                YellowColor = ColorOf(RagBand.Yellow, yellowRow),
                RedColor = ColorOf(RagBand.Red, rows.FirstOrDefault(x => x.Band == RagBand.Red)),
                GrayColor = ColorOf(RagBand.Gray, rows.FirstOrDefault(x => x.Band == RagBand.Gray)),
                Warnings = warnings
            };
        }

        private static string ColorOf(RagBand band, RagThresholdConfig? row)
            => row is not null && !string.IsNullOrWhiteSpace(row.Color) ? row.Color : ColorFor(band);

        private static string ColorFor(RagBand band) => band switch
        {
            RagBand.Green => RagThresholdDefaults.GreenColor,
            RagBand.Yellow => RagThresholdDefaults.YellowColor,
            RagBand.Red => RagThresholdDefaults.RedColor,
            _ => RagThresholdDefaults.GrayColor
        };

        /// <summary>Green has no upper bound so scores above 100 stay Green (FR-601 overachievement).</summary>
        private static decimal? MinScoreFor(RagBand band, decimal green, decimal yellow) => band switch
        {
            RagBand.Green => green,
            RagBand.Yellow => yellow,
            _ => null
        };

        private static decimal? MaxScoreFor(RagBand band, decimal green, decimal yellow) => band switch
        {
            RagBand.Yellow => green,
            RagBand.Red => yellow,
            _ => null
        };

        private static string FieldFor(string message)
            => message.Contains("Green", StringComparison.OrdinalIgnoreCase) ? "green"
             : message.Contains("Yellow", StringComparison.OrdinalIgnoreCase) ? "yellow"
             : "thresholds";
    }
}
