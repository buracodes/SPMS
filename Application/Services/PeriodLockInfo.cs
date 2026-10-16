namespace Application.Services;

public sealed record PeriodLockInfo(
    PeriodLockState State,
    DateTime EffectiveEntryOpenDate,
    DateTime EffectiveCutoffDate,
    int EffectiveEntryOpenDay,
    int EffectiveCutoffDay,
    int EffectiveReminderDaysBeforeCutoff,
    bool HasExplicitOverride,
    DateTime? LockedAtUtc,
    DateTime? UnlockedAtUtc);
