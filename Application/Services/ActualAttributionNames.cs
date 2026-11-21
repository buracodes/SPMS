using Application.Interface.IRepository;

namespace Application.Services;

/// <summary>
/// Resolves entered-by display names for a whole actual-value result set in one query.
/// Mirrors the batched owner-name lookup used by the reporting service; no per-row lookup.
/// </summary>
public static class ActualAttributionNames
{
    public static Dictionary<Guid, string> Resolve(
        IUserRepository userRepository,
        IEnumerable<Guid?> recordedBy)
    {
        var ids = recordedBy
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return userRepository.GetQueryable()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToList()
            .ToDictionary(u => u.Id, u => u.FullName);
    }
}
