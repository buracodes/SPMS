using Application.Services;
using System;

namespace Application.DTOS.PeriodLock
{
    public class PeriodLockStatusItemDto
    {
        public Guid FiscalPeriodId { get; set; }
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }
        public bool IsQuarterEnd { get; set; }
        public bool IsYearEnd { get; set; }
        public PeriodLockState State { get; set; }
        public DateTime EffectiveEntryOpenDate { get; set; }
        public DateTime EffectiveCutoffDate { get; set; }
        public int EffectiveEntryOpenDay { get; set; }
        public int EffectiveCutoffDay { get; set; }
        public int EffectiveReminderDaysBeforeCutoff { get; set; }
        public bool HasExplicitOverride { get; set; }
        public DateTime? LockedAtUtc { get; set; }
        public DateTime? UnlockedAtUtc { get; set; }
    }
}
