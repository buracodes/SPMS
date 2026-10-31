using System;
using System.Collections.Generic;

namespace Infrastructure.Options;

/// <summary>
/// Optional mapping from external HR position levels (PositionalLevel.name in ecx_hrms)
/// to permission names. Disabled by default: when <see cref="Enabled"/> is false the
/// effective-access evaluation never touches the HR database.
/// </summary>
public class HrPositionAccessOptions
{
    public const string SectionName = "HrPositionAccess";

    public bool Enabled { get; set; }

    /// <summary>How long a resolved email → position levels lookup is cached. Set 0 to disable caching.</summary>
    public int CacheMinutes { get; set; } = 5;

    /// <summary>Position level name → permission names (e.g. "Manager" → ["scorecard:view"]).</summary>
    public Dictionary<string, string[]> PositionLevelPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
