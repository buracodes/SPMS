using System;
using System.Collections.Generic;
using System.Threading;
using Application.CQRS.AccessControl;

namespace Application.Interface.IService;

/// <summary>
/// Centralized backend evaluation of permission policy (ADR-011). Both the ASP.NET
/// authorization handler and the Access Control Management page resolve effective rights
/// through this service — the precedence algorithm lives here, never duplicated in
/// controllers or Angular code. Grant results are descriptive only: they never bypass
/// resource-scoped rules (ownership, period locks, data scoping), which stay in the
/// request pipeline.
/// </summary>
public interface IEffectivePermissionService
{
    /// <summary>Returns the set of permission names the (active) user effectively holds.</summary>
    Task<IReadOnlySet<string>> GetEffectiveGrantNamesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Convenience check used by the authorization handler.</summary>
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full explanation table for the Effective Access tab: one entry per catalog right,
    /// computed by the exact precedence the enforcement-time evaluation uses.
    /// </summary>
    Task<EffectiveAccessResultDto> EvaluateDetailedAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Administrative change hook. The current implementation evaluates live from the
    /// database on every request (no cache), so revocations take effect immediately and
    /// this is a no-op; write handlers call it so a future cache layer can drop effective-
    /// rights entries the moment an assignment changes.
    /// </summary>
    void InvalidateAllAsync();
}
