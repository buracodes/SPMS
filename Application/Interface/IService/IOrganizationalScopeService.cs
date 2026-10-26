using System;
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
    /// <summary>The division the principal belongs to, when the token carries one.</summary>
    Guid? DivisionId { get; }

    /// <summary>True when the principal effectively holds corporate-wide visibility.</summary>
    Task<bool> HasCorporateAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the principal may access the given division's data.</summary>
    Task<bool> CanAccessDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="Exceptions.ForbiddenException"/> when the division is out of scope.</summary>
    Task EnsureDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default);

    /// <summary>True when the principal owns the KPI directly or through its division.</summary>
    Task<bool> IsAssignedKpiAsync(Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default);
}
