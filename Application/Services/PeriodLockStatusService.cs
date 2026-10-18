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
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IBusinessClock businessClock;
    private readonly ILogger<PeriodLockStatusService> _logger;

    public PeriodLockStatusService(
        IFiscalPeriodRepository fiscalPeriodRepository,
        IPeriodLockConfigRepository periodLockConfigRepository,
        ISystemConfigRepository systemConfigRepository,
        IFiscalYearRepository fiscalYearRepository,
        IBusinessClock businessClock,
        ILogger<PeriodLockStatusService> logger)
    {
        this.fiscalPeriodRepository = fiscalPeriodRepository;
        this.periodLockConfigRepository = periodLockConfigRepository;
        this.systemConfigRepository = systemConfigRepository;
        this.fiscalYearRepository = fiscalYearRepository;
        this.businessClock = businessClock;
        this._logger = logger;
    }

    public async Task<PeriodLockInfo> GetStatusAsync(Guid scorecardId, Guid fiscalPeriodId, DateTime? asOfBusinessDate = null)
    {
        var period = await fiscalPeriodRepository.GetById(fiscalPeriodId);
        if (period == null)
        {
            throw new Exceptions.NotFoundException(nameof(FiscalPeriod), fiscalPeriodId);
        }

        return await GetStatusAsync(scorecardId, period, asOfBusinessDate);
    }

    public async Task<PeriodLockInfo> GetStatusAsync(Guid scorecardId, FiscalPeriod period, DateTime? asOfBusinessDate = null)
    {
        var explicitConfig = await periodLockConfigRepository
            .GetByScorecardAndPeriodAsync(scorecardId, period.Id);
        var defaults = await GetDefaultWindowAsync();
        var fiscalYear = await fiscalYearRepository.GetById(period.FiscalYearId);

        return Evaluate(period, explicitConfig, defaults, fiscalYear, asOfBusinessDate ?? businessClock.BusinessDate);
    }

    public async Task<IReadOnlyList<PeriodLockInfo>> GetStatusesAsync(
        Guid scorecardId,
        IReadOnlyList<FiscalPeriod> periods,
        IReadOnlyList<PeriodLockConfig> explicitConfigs,
        DateTime? asOfBusinessDate = null)
    {
        var defaults = await GetDefaultWindowAsync();
        var configByPeriod = explicitConfigs.ToDictionary(c => c.FiscalPeriodId);
        var businessDate = asOfBusinessDate ?? businessClock.BusinessDate;

        // A batch normally covers one fiscal year; resolve each distinct year once so the
        // final-month grace rule can be applied to the last reporting month only.
        var fiscalYears = new Dictionary<Guid, FiscalYear?>();
        foreach (var fiscalYearId in periods.Select(p => p.FiscalYearId).Distinct())
        {
            fiscalYears[fiscalYearId] = await fiscalYearRepository.GetById(fiscalYearId);
        }

        return periods
            .Select(p => Evaluate(
                p,
                configByPeriod.GetValueOrDefault(p.Id),
                defaults,
                fiscalYears.GetValueOrDefault(p.FiscalYearId),
                businessDate))
            .ToList();
    }

    private PeriodLockInfo Evaluate(
        FiscalPeriod period,
        PeriodLockConfig? explicitConfig,
        (int EntryOpenDay, int CutoffDay, int ReminderDaysBeforeCutoff) defaults,
        FiscalYear? fiscalYear,
        DateTime businessDate)
    {
        // Fiscal periods are stored with DateTimeKind.Unspecified and represent ECX business
        // calendar dates; compare against the business date's calendar day only.
        var businessDay = businessDate.Date;

        var entryOpenDay = explicitConfig?.EntryOpenDay ?? defaults.EntryOpenDay;
        var cutoffDay = explicitConfig?.CutoffDay ?? defaults.CutoffDay;
        var reminderDays = explicitConfig?.ReminderDaysBeforeCutoff ?? defaults.ReminderDaysBeforeCutoff;

        var effectiveEntryOpenDate = period.EndDate.Date.AddDays(entryOpenDay);
        var effectiveCutoffDate = period.EndDate.Date.AddDays(cutoffDay);
        var graceApplied = false;

        // Final reporting month (confirmed business rule): actual entry for the fiscal
        // year's last month stays open through the grace deadline — end date + 7 calendar
        // days, inclusive — even if the configured cutoff would close it earlier. The end
        // date itself is never moved. Explicit period-lock configs still take precedence,
        // keeping admin locks and authorized extensions intact.
        if (explicitConfig is null && fiscalYear is not null
            && period.EndDate.Date == fiscalYear.EndDate.Date)
        {
            effectiveCutoffDate = fiscalYear.FinalMonthGraceDeadline;
            graceApplied = true;
        }

        var hasExplicitConfig = explicitConfig != null;
        var lockedAt = explicitConfig?.LockedAtUtc;
        var unlockedAt = explicitConfig?.UnlockedAtUtc;

        _logger.LogInformation(
            "[PeriodLock] Period={Month}, EndDate={EndDate}, EntryOpenDay={EntryOpenDay}, CutoffDay={CutoffDay}, " +
            "EffectiveOpen={EffectiveOpen}, EffectiveCutoff={EffectiveCutoff}, GraceApplied={GraceApplied}, " +
            "BusinessDate={BusinessDate}, LockedAt={LockedAt}, UnlockedAt={UnlockedAt}, State={State}",
            period.FiscalMonth, period.EndDate.ToString("yyyy-MM-dd"), entryOpenDay, cutoffDay,
            effectiveEntryOpenDate.ToString("yyyy-MM-dd"), effectiveCutoffDate.ToString("yyyy-MM-dd"), graceApplied,
            businessDay.ToString("yyyy-MM-dd"),
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
        if (businessDay > effectiveCutoffDate)
        {
            _logger.LogInformation("[PeriodLock] Period={Month}: State=Locked (past cutoff {CutoffDate}, business date {BusinessDate})", period.FiscalMonth, effectiveCutoffDate.ToString("yyyy-MM-dd"), businessDay.ToString("yyyy-MM-dd"));
            return new PeriodLockInfo(PeriodLockState.Locked, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
        }

        if (businessDay < effectiveEntryOpenDate)
        {
            _logger.LogInformation("[PeriodLock] Period={Month}: State=NotYetOpen (entry opens {OpenDate}, business date {BusinessDate})", period.FiscalMonth, effectiveEntryOpenDate.ToString("yyyy-MM-dd"), businessDay.ToString("yyyy-MM-dd"));
            return new PeriodLockInfo(PeriodLockState.NotYetOpen, effectiveEntryOpenDate, effectiveCutoffDate, entryOpenDay, cutoffDay, reminderDays, hasExplicitConfig, lockedAt, unlockedAt);
        }

        _logger.LogInformation("[PeriodLock] Period={Month}: State=Open (entry window {Open} to {Cutoff}, business date {BusinessDate})", period.FiscalMonth, effectiveEntryOpenDate.ToString("yyyy-MM-dd"), effectiveCutoffDate.ToString("yyyy-MM-dd"), businessDay.ToString("yyyy-MM-dd"));
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
