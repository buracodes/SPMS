using System;
using System.Collections.Generic;

namespace Application.DTOS.PeriodLock
{
    public class GetPeriodLockStatusDto
    {
        public Guid ScorecardId { get; set; }
        public Guid FiscalYearId { get; set; }
        public List<PeriodLockStatusItemDto> Periods { get; set; } = new();
    }
}
