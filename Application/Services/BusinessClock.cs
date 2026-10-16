using Application.Interface.IService;

namespace Application.Services;

/// <summary>
/// Production <see cref="IBusinessClock"/>. ECX operates in East Africa Time (EAT,
/// UTC+3). Ethiopia follows a fixed offset year-round, so the offset is a constant
/// and does not need DST-aware zone conversions.
/// </summary>
public sealed class BusinessClock : IBusinessClock
{
    /// <summary>ECX business timezone offset from UTC (East Africa Time / Ethiopia).</summary>
    public const int UtcOffsetHours = 3;

    private readonly TimeSpan offset;

    public BusinessClock(TimeSpan offset)
    {
        this.offset = offset;
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime BusinessDate => DateTime.UtcNow.Add(offset).Date;
}