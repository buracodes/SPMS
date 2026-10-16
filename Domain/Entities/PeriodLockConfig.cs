using Domain.Common;

namespace Domain.Entities;

public class PeriodLockConfig : CommonEntity
{
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid FiscalPeriodId { get; set; }
    public FiscalPeriod? FiscalPeriod { get; set; }

    public int EntryOpenDay { get; set; } = 1;
    public int CutoffDay { get; set; } = 7;
    public int ReminderDaysBeforeCutoff { get; set; } = 2;

    public DateTime? LockedAtUtc { get; set; }
    public Guid? LockedByUserId { get; set; }
    public User? LockedByUser { get; set; }

    public DateTime? UnlockedAtUtc { get; set; }
    public Guid? UnlockedByUserId { get; set; }
    public User? UnlockedByUser { get; set; }
}
