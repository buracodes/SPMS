using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interface.IService;

/// <summary>
/// Centralizes organizational data scoping: a principal with a division claim is limited to
/// that division, while a corporate principal needs the explicit monitoring permission to
/// read across divisions. Resource ownership rules (KPI owner/division owner) remain in the
/// dedicated guards.
/// <para>
/// Corporate visibility is resolved from the live authorization state
/// (<see cref="IEffectivePermissionService"/>), never from the permission claims minted into
/// the JWT. A revoked monitoring permission therefore takes effect on the next request
/// (ADR-011), exactly like the endpoint authorization handler.
/// </para>
/// </summary>
public interface IOrganizationalScopeService
{
    /// <summary>The authenticated user id, when present.</summary>
    Guid? UserId { get; }

    /// <summary>The division the principal currently belongs to, resolved from current state.</summary>
    Guid? DivisionId { get; }

    /// <summary>
    /// Every division in the principal's live scope: the HR primary division first, followed
    /// by explicitly authorized divisions. Empty for a corporate principal.
    /// </summary>
    IReadOnlyList<Guid> DivisionIds { get; }

    /// <summary>True when the principal effectively holds corporate-wide visibility.</summary>
    Task<bool> HasCorporateAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the principal may browse the division portfolio (division list, corporate
    /// goals in a division plan, division report). Corporate roles qualify through
    /// <c>performance:monitor.view</c>; read-only corporate roles (View Only) qualify through
    /// <c>divisions:view</c>. A principal with a division claim is always limited to its own
    /// division, so this returns false for division accounts.
    /// </summary>
    Task<bool> HasDivisionPortfolioAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the principal may access the given division's data.</summary>
    Task<bool> CanAccessDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="Exceptions.ForbiddenException"/> when the division is out of scope.</summary>
    Task EnsureDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default);

    /// <summary>True when the principal owns the KPI directly or through its division.</summary>
    Task<bool> IsAssignedKpiAsync(Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a corporate principal holds the live <c>initiatives:view</c> right. A
    /// division-scoped principal is always limited to its own/its division's initiatives, so
    /// this is false for division accounts even when a group grants the right.
    /// </summary>
    Task<bool> CanAccessInitiativePortfolioAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the principal may read the given initiative: its owner, its division owner,
    /// or a corporate principal. A division-scoped principal is never widened by a group grant.
    /// </summary>
    Task<bool> CanAccessInitiativeAsync(Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="Exceptions.ForbiddenException"/> when the initiative is out of scope.</summary>
    Task EnsureInitiativeAsync(Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default);
}
