using Application.Interface.IService;
using Application.Services;

namespace Application.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="IBusinessClock"/> for tests. ECX business time is East
/// Africa Time (UTC+3), which is what <see cref="BusinessDate"/> applies by default.
/// </summary>
internal sealed class TestBusinessClock : IBusinessClock
{
    public DateTime UtcNow { get; set; }
    public DateTime BusinessDate { get; set; }

    /// <summary>
    /// Clock aligned with the real current instant in ECX business time. Safe for tests
    /// whose fiscal years are far in the future (e.g. 2099).
    /// </summary>
    public static TestBusinessClock Real()
    {
        var utcNow = DateTime.UtcNow;
        return new TestBusinessClock
        {
            UtcNow = utcNow,
            BusinessDate = utcNow.AddHours(BusinessClock.UtcOffsetHours).Date
        };
    }

    /// <summary>
    /// Fixed-instant clock. <paramref name="utcNow"/> is the current UTC instant and the
    /// business date is derived by adding <paramref name="utcOffsetHours"/> (default: EAT).
    /// </summary>
    public static TestBusinessClock On(DateTime utcNow, int utcOffsetHours = 3)
        => new()
        {
            UtcNow = utcNow,
            BusinessDate = utcNow.AddHours(utcOffsetHours).Date
        };
}