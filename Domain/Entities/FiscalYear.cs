using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class FiscalYear : CommonEntity
{
    /// <summary>
    /// Confirmed business rule: actual entry for the final reporting month stays open
    /// for this many calendar days after the fiscal year's end date, inclusive. The
    /// end date itself is never moved; the grace only widens the final entry window.
    /// </summary>
    public const int FinalMonthGraceDays = 7;

    public required string Label { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public FiscalYearStatus FiscalYearStatus { get; set; } = FiscalYearStatus.Open;
    public ICollection<FiscalPeriod> Periods { get; set; } = new List<FiscalPeriod>();
    public ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();

    /// <summary>
    /// The last calendar day (inclusive) on which actual entry for the final reporting
    /// month is allowed. Derived dynamically from the configured end date — a fiscal year
    /// ending August 31, 2027 therefore has a grace deadline of September 7, 2027, and
    /// normal entry is blocked from September 8. No month-of-year is hardcoded: any
    /// twelve-month fiscal calendar gets the same seven-day grace.
    /// </summary>
    public DateTime FinalMonthGraceDeadline => EndDate.Date.AddDays(FinalMonthGraceDays);

    /// <summary>
    /// True when the final-month grace period has expired in ECX business time, i.e.
    /// the business date is past the grace deadline (the first day normal entry is
    /// blocked). This is the closure boundary for the fiscal year and all of the
    /// scorecards it owns.
    /// </summary>
    public bool IsPastFinalMonthGrace(DateTime businessDate)
    {
        return businessDate.Date > FinalMonthGraceDeadline;
    }
}
