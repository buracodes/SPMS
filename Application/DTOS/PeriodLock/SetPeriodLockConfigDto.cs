using System;

namespace Application.DTOS.PeriodLock
{
    public class SetPeriodLockConfigDto
    {
        public Guid ScorecardId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public int? EntryOpenDay { get; set; }
        public int? CutoffDay { get; set; }
        public int? ReminderDaysBeforeCutoff { get; set; }
        public bool Lock { get; set; }
        public bool Unlock { get; set; }
        public bool ForceOpen { get; set; }
    }
}
