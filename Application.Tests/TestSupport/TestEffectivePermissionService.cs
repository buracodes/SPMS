using Application.CQRS.AccessControl;
using Application.Interface.IService;

namespace Application.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IEffectivePermissionService"/>: the live authorization state used by
/// scope tests. Grants are per user, so a test can prove that a permission claim in the token
/// does not grant corporate visibility when the live state has revoked it.
/// </summary>
internal sealed class TestEffectivePermissionService : IEffectivePermissionService
{
    private readonly Dictionary<Guid, HashSet<string>> grants = new();

    public void Grant(Guid userId, string permission)
    {
        if (!grants.TryGetValue(userId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            grants[userId] = set;
        }

        set.Add(permission);
    }

    public void Revoke(Guid userId, string permission)
    {
        if (grants.TryGetValue(userId, out var set))
        {
            set.Remove(permission);
        }
    }

    public Task<IReadOnlySet<string>> GetEffectiveGrantNamesAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> set = grants.TryGetValue(userId, out var granted)
            ? new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(set);
    }

    public Task<bool> HasPermissionAsync(
        Guid userId, string permission, CancellationToken cancellationToken = default)
        => Task.FromResult(
            grants.TryGetValue(userId, out var set) && set.Contains(permission));

    public Task<EffectiveAccessResultDto> EvaluateDetailedAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("TestEffectivePermissionService only evaluates single-permission checks.");

    public void InvalidateAllAsync()
    {
    }
}
