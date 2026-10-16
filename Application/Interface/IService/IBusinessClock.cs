namespace Application.Interface.IService;

/// <summary>
/// Single source of "now" for ECX business rules. The business date is evaluated in
/// East Africa Time (EAT / UTC+3, Ethiopia — no daylight saving), so a fiscal year or
/// period boundary that passes at 23:00 UTC is already the next calendar day in ET
/// and the system behaves accordingly before the overnight closure job runs.
/// </summary>
public interface IBusinessClock
{
    /// <summary>Current instant in UTC.</summary>
    DateTime UtcNow { get; }

    /// <summary>Current calendar date in ECX business time (East Africa Time, UTC+3).</summary>
    DateTime BusinessDate { get; }
}