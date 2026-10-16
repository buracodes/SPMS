using System;

namespace Application.Exceptions
{
    public class PeriodLockedException : ApplicationException
    {
        public Guid FiscalPeriodId { get; }
        public Guid ScorecardId { get; }

        public PeriodLockedException(Guid scorecardId, Guid fiscalPeriodId, string message) : base(message)
        {
            ScorecardId = scorecardId;
            FiscalPeriodId = fiscalPeriodId;
        }
    }
}
