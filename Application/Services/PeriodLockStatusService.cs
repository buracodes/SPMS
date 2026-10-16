using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Constants;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Application.Services;

public sealed class PeriodLockStatusService : IPeriodLockStatusService
{
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IPeriodLockConfigRepository periodLockConfigRepository;
    private readonly ISystemConfigRepository systemConfigRepository;
    private readonly ILogger<PeriodLockStatusService> _logger;

    public PeriodLockStatusService(
        IFiscalPeriodRepository fiscalPeriodRepository,
        IPeriodLockConfigRepository periodLockConfigRepository,
        ISystemConfigRepository systemConfigRepository,
        ILogger<PeriodLockStatusService> logger)
    {
        this.fiscalPeriodRepository = fiscalPeriodRepository;
        this.periodLockConfigRepository = periodLockConfigRepository;
        this.systemConfigRepository = systemConfigRepository;
        this._logger = logger;
    }

    public async Task<PeriodLockInfo> GetStatusAsync(Guid scorecardId, Guid fiscalPeriodId, DateTime? nowUtc = null)
    {
        var period = await fiscalPeriodRepository.GetById(fiscalPeriodId);
        if (period == null)
        {
            throw new Exceptions.NotFoundException(nameof(FiscalPeriod), fiscalPeriodId);
        }

        return await GetStatusAsync(scorecardId, period, nowUtc);
    }

    public async Task<PeriodLockInfo> GetStatusAsync(Guid scorecardId, FiscalPeriod period, DateTime? nowUtc = null)
    {
        var explicitConfig = await periodLockConfigRepository
            .GetByScorecardAndPeriodAsync(scorecardId, period.Id);
        var defaults = await GetDefaultWindowAsync();

        return Evaluate(period, explicitConfig, defaults, nowUtc ?? DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<PeriodLockInfo>> GetStatusesAsync(
        Guid scorecardId,
        IReadOnlyList<FiscalPeriod> periods,
        IReadOnlyList<PeriodLockConfig> explicitConfigs,
        DateTime? nowUtc = null)
    {
        var defaults = await GetDefaultWindowAsync();
        var configByPeriod = explicitConfigs.ToDictionary(c => c.FiscalPeriodId);
        var now = nowUtc ?? DateTime.UtcNow;

        return periods
            .Select(p => Evaluate(p, configByPeriod.GetValueOrDefault(p.Id), defaults, now))
            .ToList();
    }

    private PeriodLockInfo Evaluate(
        FiscalPeriod period,
        PeriodLockConfig? explicitConfig,
        (int EntryOpenDay, int CutoffDay, int ReminderDaysBeforeCutoff) defaults,
        DateTime nowUtc)
    {
        // Use DateTime.Today for comparison since fiscal periods are stored with DateTimeKind.Unspecified
        // and represent local calendar dates. This avoids UTC vs Local timezone mismatches.
        var nowLocal = nowUtc.Date;

        var entryOpenDay = explicitConfig?.EntryOpenDay ?? defaults.EntryOpenDay;
        var cutoffDay = explicitConfig?.CutoffDay ?? defaults.CutoffDay;
        var reminderDays = explicitConfig?.ReminderDaysBeforeCutoff ?? defaults.ReminderDaysBeforeCutoff;

        var effectiveEntryOpenDate = period.EndDate.Date.AddDays(entryOpenDay);
        var effectiveCutoffDate = period.EndDate.Date.AddDays(cutoffDay);

        var hasExplicitConfig = explicitConfig != null;
        var lockedAt = explicitConfig?.LockedAtUtc;
        var unlockedAt = explicitConfig?.UnlockedAtUtc;

        _logger.LogInformation(
            "[PeriodLock] Period={Month}, EndDate={EndDate}, EntryOpenDay={EntryOpenDay}, CutoffDay={CutoffDay}, " +
            "EffectiveOpen={EffectiveOpen}, EffectiveCutoff={EffectiveCutoff}, NowLocal={NowLocal}, " +
            "LockedAt={LockedAt}, UnlockedAt={UnlockedAt}, State={State}",
            period.FiscalMonth, period.EndDate.ToString("yyyy-MM-dd"), entryOpenDay, cutoffDay,
            effectiveEntryOpenDate.ToString("yyyy-MM-dd"), effectiveCutoffDate.ToString("yyyy-MM-dd"),
            nowLocal.ToString("yyyy-MM-dd"),
            lockedAt?.ToString("yyyy-MM-dd HH:mm") ?? "null",
            unlockedAt?.ToString("yyyy-MM-dd HH:mm") ?? "null",
            "evaluating");

        // Explicit lock takes precedence
        if (lockedAt is not null)
        {
            if (unlockedAt is null || unlockedAt < lockedAt)
            {
                _logger.LogInformation("[PeriodLock] Period={Month}: State=Locked (explicit lock)", period.FiscalMonth);
                return new PeriodLockInfo(PeriodLockState.Locked, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
            }

            // Explicitly unlocked (force open) — ignore date window
            _logger.LogInformation("[PeriodLock] Period={Month}: State=Open (explicit unlock/force open)", period.FiscalMonth);
            return new PeriodLockInfo(PeriodLockState.Open, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
        }

        // No explicit lock: evaluate based on date window
        if (nowLocal > effectiveCutoffDate)
        {
            _logger.LogInformation("[PeriodLock] Period={Month}: State=Locked (past cutoff {CutoffDate}, now {Now})", period.FiscalMonth, effectiveCutoffDate.ToString("yyyy-MM-dd"), nowLocal.ToString("yyyy-MM-dd"));
            return new PeriodLockInfo(PeriodLockState.Locked, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
        }

        if (nowLocal < effectiveEntryOpenDate)
        {
            _logger.LogInformation("[PeriodLock] Period={Month}: State=NotYetOpen (entry opens {OpenDate}, now {Now})", period.FiscalMonth, effectiveEntryOpenDate.ToString("yyyy-MM-dd"), nowLocal.ToString("yyyy-MM-dd"));
            return new PeriodLockInfo(PeriodLockState.NotYetOpen, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
        }

        _logger.LogInformation("[PeriodLock] Period={Month}: State=Open (entry window {Open} to {Cutoff}, now {Now})", period.FiscalMonth, effectiveEntryOpenDate.ToString("yyyy-MM-dd"), effectiveCutoffDate.ToString("yyyy-MM-dd"), nowLocal.ToString("yyyy-MM-dd"));
        return new PeriodLockInfo(PeriodLockState.Open, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
    }

    private async Task<(int EntryOpenDay, int CutoffDay, int ReminderDaysBeforeCutoff)> GetDefaultWindowAsync()
    {
        var keys = new[] { SystemConfigKeys.EntryOpenDay, SystemConfigKeys.CutoffDay, SystemConfigKeys.ReminderDaysBeforeCutoff };
        var configs = await systemConfigRepository.GetManyAsync(keys);
        var byKey = configs.ToDictionary(c => c.Key, c => c.ValueJson);

        var entryOpenDay = TryReadInt(byKey, SystemConfigKeys.EntryOpenDay) ?? 1;
        var cutoffDay = TryReadInt(byKey, SystemConfigKeys.CutoffDay) ?? 7;
        var reminderDays = TryReadInt(byKey, SystemConfigKeys.ReminderDaysBeforeCutoff) ?? 2;

        return (entryOpenDay, cutoffDay, reminderDays);
    }

    private static int? TryReadInt(IReadOnlyDictionary<string, string> byKey, string key)
    {
        if (!byKey.TryGetValue(key, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (int.TryParse(json.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
            {
                return plain;
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Number => document.RootElement.GetInt32(),
                JsonValueKind.String => int.TryParse(document.RootElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
