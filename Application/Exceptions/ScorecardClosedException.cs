using System;

namespace Application.Exceptions
{
    /// <summary>
    /// Thrown when a write targets a scorecard (or its fiscal year) that has been
    /// automatically closed at year end. Closed scorecards remain viewable,
    /// reportable and searchable but are read-only (FR-703).
    /// </summary>
    public class ScorecardClosedException : ApplicationException
    {
        public Guid? ScorecardId { get; }
        public Guid? FiscalYearId { get; }

        public ScorecardClosedException(string message, Guid? scorecardId = null, Guid? fiscalYearId = null) : base(message)
        {
            ScorecardId = scorecardId;
            FiscalYearId = fiscalYearId;
        }
    }
}