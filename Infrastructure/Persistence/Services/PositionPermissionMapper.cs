using System;
using System.Collections.Generic;

namespace Infrastructure.Persistence.Services;

/// <summary>
/// Pure mapping from HR position levels to permission names. Kept free of EF/HR
/// dependencies so the rule can be unit-tested without a database.
/// </summary>
public static class PositionPermissionMapper
{
    public static IReadOnlySet<string> Map(
        IEnumerable<string?> positionLevels,
        IReadOnlyDictionary<string, string[]> levelPermissions)
    {
        var grants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (levelPermissions.Count == 0)
        {
            return grants;
        }

        // Configuration binding hands back a case-sensitive dictionary, so normalize here.
        var byLevel = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (level, permissions) in levelPermissions)
        {
            if (string.IsNullOrWhiteSpace(level))
            {
                continue;
            }

            byLevel[level.Trim()] = permissions ?? Array.Empty<string>();
        }

        foreach (var level in positionLevels)
        {
            if (string.IsNullOrWhiteSpace(level) || !byLevel.TryGetValue(level.Trim(), out var permissions))
            {
                continue;
            }

            foreach (var permission in permissions)
            {
                if (!string.IsNullOrWhiteSpace(permission))
                {
                    grants.Add(permission.Trim());
                }
            }
        }

        return grants;
    }
}
