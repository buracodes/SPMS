using Application.Exceptions;
using Application.Interface.IService;

namespace Application.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IOrganizationalScopeService"/> for handler tests. It defaults to
/// corporate access so fixtures that do not model a division principal keep exercising the
/// handler logic; scope-authorization tests set the properties explicitly (and never rely on
/// a missing dependency silently disabling the check).
/// </summary>
internal sealed class TestOrganizationalScopeService : IOrganizationalScopeService
{
    public Guid? UserId { get; set; }
    public Guid? DivisionId { get; set; }
    public bool HasCorporateAccess { get; set; } = true;

    public Task<bool> HasCorporateAccessAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HasCorporateAccess);

    public Task<bool> CanAccessDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
        => Task.FromResult(HasCorporateAccess || (DivisionId.HasValue && DivisionId.Value == divisionId));

    public Task EnsureDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
    {
        if (!HasCorporateAccess && (!DivisionId.HasValue || DivisionId.Value != divisionId))
        {
            throw new ForbiddenException("You do not have access to this division's data.");
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsAssignedKpiAsync(
        Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default)
    {
        if (!DivisionId.HasValue)
        {
            return Task.FromResult(HasCorporateAccess);
        }

        var userOwned = ownerId.HasValue && UserId.HasValue && ownerId.Value == UserId.Value;
        var divisionOwned = divisionOwnerId.HasValue && divisionOwnerId.Value == DivisionId.Value;
        return Task.FromResult(userOwned || divisionOwned);
    }
}
