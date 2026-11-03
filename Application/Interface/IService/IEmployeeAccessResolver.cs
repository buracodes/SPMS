using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IService;

/// <summary>
/// Resolves whether a principal is a currently active HR employee and, when so, the single
/// division that employee works in. HR is authoritative for both facts: the local account's
/// <c>DivisionId</c> column is not maintained by the HR synchronization and must never be
/// used as an authorization scope.
/// <para>
/// The lookup is read-only against HR and fails closed — an HR outage or an unsynchronized
/// division yields no baseline access and no division scope rather than an assumed one.
/// </para>
/// </summary>
public interface IEmployeeAccessResolver
{
    Task<EmployeeAccessSnapshot> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <paramref name="IsActiveHrEmployee"/> is true only when HR holds an active employee row for
/// the identity. <paramref name="DivisionId"/> is set only when that employee's active position
/// points at a local, active, HR-synchronized division; it is the primary division.
/// <paramref name="DivisionIds"/> carries the additional divisions explicitly authorized for
/// the account; it never replaces or widens the HR primary division.
/// </summary>
public sealed record EmployeeAccessSnapshot(
    bool IsActiveHrEmployee,
    Guid? DivisionId,
    IReadOnlyList<Guid>? DivisionIds = null)
{
    public static readonly EmployeeAccessSnapshot None = new(false, null);

    /// <summary>The effective division scope: the HR primary division first, then explicit grants.</summary>
    public IReadOnlyList<Guid> ScopedDivisionIds
    {
        get
        {
            var ids = new List<Guid>(1 + (DivisionIds?.Count ?? 0));
            if (DivisionId.HasValue && DivisionId.Value != Guid.Empty)
            {
                ids.Add(DivisionId.Value);
            }

            if (DivisionIds is not null)
            {
                foreach (var id in DivisionIds)
                {
                    if (id != Guid.Empty && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            return ids;
        }
    }
}
