using Domain.Enums;
using System;
using System.Linq;

namespace Application.Dashboard;

/// <summary>Friendly audit descriptions shared by the dashboard and the legacy dashboard endpoints.</summary>
public static class AuditDescription
{
    public static string For(string entityType, AuditAction action)
    {
        var friendly = FriendlyEntityName(entityType);

        return action switch
        {
            AuditAction.Create => $"Created {friendly}",
            AuditAction.Update => $"Updated {friendly}",
            AuditAction.Delete => $"Deleted {friendly}",
            _ => $"Modified {friendly}"
        };
    }

    private static string FriendlyEntityName(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return "record";
        }

        var plural = entityType.EndsWith("y", StringComparison.Ordinal)
            ? entityType[..^1] + "ies"
            : entityType.EndsWith("s", StringComparison.Ordinal)
                ? entityType
                : entityType + "s";

        var spaced = string.Concat(plural.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

        return spaced.ToLowerInvariant();
    }
}
