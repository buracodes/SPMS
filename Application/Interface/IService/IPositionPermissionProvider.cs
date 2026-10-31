using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IService;

/// <summary>
/// Resolves the permission names granted by a user's HR position level (optional
/// integration, disabled by default). Grants returned here are evaluated with the same
/// precedence as a group grant: an explicit user- or group-level deny still wins.
/// Implementations must degrade gracefully — an unavailable HR database yields no grants.
/// </summary>
public interface IPositionPermissionProvider
{
    /// <summary>
    /// Effective permission names implied by the given employee's position levels. The identifier
    /// is the AD object GUID, which is also HR's <c>Employee.EmpId</c>; HR email addresses are too
    /// inconsistent to join on.
    /// </summary>
    Task<IReadOnlySet<string>> GetGrantedPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
