using Domain.Entities;

namespace Application.CQRS.KpiActuals;

/// <summary>One revision plus its position in the chain. Oldest = 1, newest = N.</summary>
public sealed record ActualRevision(KpiActualValue Value, int RevisionNumber);

/// <summary>
/// Ordered revision chain shared by corporate and division history. Even when the stored
/// links are inconsistent, every available revision is retained and an integrity flag is
/// raised so the caller can surface a warning instead of silently dropping rows.
/// </summary>
public sealed class ActualRevisionChain
{
    public ActualRevisionChain(
        IReadOnlyList<ActualRevision> revisions,
        bool hasIntegrityIssues)
    {
        Revisions = revisions;
        HasIntegrityIssues = hasIntegrityIssues;
    }

    public IReadOnlyList<ActualRevision> Revisions { get; }

    public bool HasIntegrityIssues { get; }
}

/// <summary>
/// Traverses the append-only SupersedesActualId links. Links alone establish revision order;
/// when the stored chain is damaged, every row is retained, an integrity flag is raised, and
/// only stable row identity is used for a deterministic fallback order. Timestamps are never
/// used to infer supersession or revision order.
/// </summary>
public static class ActualRevisionHistory
{
    public static ActualRevisionChain Build(IReadOnlyList<KpiActualValue> values)
    {
        if (values.Count == 0)
        {
            return new ActualRevisionChain(Array.Empty<ActualRevision>(), false);
        }

        var issues = false;

        // Duplicate ids cannot occur in a valid table, but a defensive de-duplication keeps
        // a corrupted read result from throwing here or producing duplicate history rows.
        // Prefer the current row, then order by id only; never pick by timestamp.
        var distinct = values
            .GroupBy(v => v.Id)
            .Select(group => group
                .OrderByDescending(v => v.IsCurrent)
                .ThenByDescending(v => v.SupersedesActualId.HasValue)
                .ThenBy(v => v.Id)
                .First())
            .ToList();

        if (distinct.Count != values.Count)
        {
            issues = true;
        }

        var byId = distinct.ToDictionary(v => v.Id);
        var supersededBy = new Dictionary<Guid, List<KpiActualValue>>();
        foreach (var value in distinct.Where(v => v.SupersedesActualId.HasValue))
        {
            var previousId = value.SupersedesActualId!.Value;
            if (!supersededBy.TryGetValue(previousId, out var children))
            {
                children = new List<KpiActualValue>();
                supersededBy[previousId] = children;
            }

            children.Add(value);
        }

        if (supersededBy.Values.Any(children => children.Count > 1))
        {
            issues = true;
        }

        if (distinct.Any(v => v.SupersedesActualId is { } previousId && !byId.ContainsKey(previousId)))
        {
            issues = true;
        }

        var currentCount = distinct.Count(v => v.IsCurrent);
        if (currentCount != 1)
        {
            issues = true;
        }

        // A tip is not superseded by another row in this result. Normally it is the single
        // current revision; a damaged chain can expose several tips, all shown with a warning.
        var tips = distinct
            .Where(v => !supersededBy.ContainsKey(v.Id))
            .OrderByDescending(v => v.IsCurrent)
            .ThenBy(v => v.Id)
            .ToList();

        var visited = new HashSet<Guid>();
        var ordered = new List<KpiActualValue>(distinct.Count);

        foreach (var tip in tips)
        {
            WalkBackwards(tip, byId, visited, ordered, ref issues);
        }

        // Cyclic links have no tip. Append those rows deterministically rather than losing
        // them; the integrity flag tells the UI the chain is incomplete.
        foreach (var remainder in distinct
                     .Where(v => !visited.Contains(v.Id))
                     .OrderByDescending(v => v.IsCurrent)
                     .ThenBy(v => v.Id)
                     .ToList())
        {
            WalkBackwards(remainder, byId, visited, ordered, ref issues);
        }

        var revisions = new List<ActualRevision>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            revisions.Add(new ActualRevision(ordered[index], ordered.Count - index));
        }

        return new ActualRevisionChain(revisions, issues);
    }

    private static void WalkBackwards(
        KpiActualValue start,
        IReadOnlyDictionary<Guid, KpiActualValue> byId,
        HashSet<Guid> visited,
        List<KpiActualValue> ordered,
        ref bool issues)
    {
        KpiActualValue? cursor = start;
        while (cursor is not null && visited.Add(cursor.Id))
        {
            ordered.Add(cursor);

            if (cursor.SupersedesActualId is not { } previousId)
            {
                break;
            }

            if (!byId.TryGetValue(previousId, out var previous))
            {
                break;
            }

            if (visited.Contains(previousId))
            {
                // Cycle or two chains converging on the same predecessor.
                issues = true;
                break;
            }

            cursor = previous;
        }
    }
}
