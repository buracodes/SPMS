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
    private Guid? divisionId;
    private IReadOnlyList<Guid> divisionIds = Array.Empty<Guid>();

    public Guid? UserId { get; set; }

    /// <summary>
    /// Primary division. Production always folds it into <see cref="DivisionIds"/>, so tests
    /// that set only the primary keep the same contract without duplicating it.
    /// </summary>
    public Guid? DivisionId
    {
        get => divisionId ?? (divisionIds.Count > 0 ? divisionIds[0] : null);
        set => divisionId = value;
    }

    public IReadOnlyList<Guid> DivisionIds
    {
        get
        {
            if (divisionIds.Count == 0)
            {
                return divisionId.HasValue ? new[] { divisionId.Value } : Array.Empty<Guid>();
            }

            if (divisionId.HasValue && !divisionIds.Contains(divisionId.Value))
            {
                var ids = new List<Guid>(divisionIds.Count + 1) { divisionId.Value };
                ids.AddRange(divisionIds);
                return ids;
            }

            return divisionIds;
        }
        set
        {
            divisionIds = value;
            if (divisionId is null && value.Count > 0)
            {
                divisionId = value[0];
            }
        }
    }

    public bool HasCorporateAccess { get; set; } = true;
    public bool HasDivisionPortfolioAccess { get; set; } = true;
    public bool HasInitiativePortfolioAccess { get; set; } = true;

    private bool HasDivisionScope => DivisionIds.Count > 0;

    private bool IsInDivisionScope(Guid? divisionId)
        => divisionId.HasValue && DivisionIds.Contains(divisionId.Value);

    public Task<bool> HasCorporateAccessAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HasCorporateAccess);

    public Task<bool> HasDivisionPortfolioAccessAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HasDivisionPortfolioAccess);

    public Task<bool> CanAccessDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
        => Task.FromResult(HasCorporateAccess || IsInDivisionScope(divisionId));

    public Task EnsureDivisionAsync(Guid divisionId, CancellationToken cancellationToken = default)
    {
        if (!HasCorporateAccess && !IsInDivisionScope(divisionId))
        {
            throw new ForbiddenException("You do not have access to this division's data.");
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsAssignedKpiAsync(
        Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default)
    {
        if (!HasDivisionScope)
        {
            return Task.FromResult(HasCorporateAccess);
        }

        var userOwned = ownerId.HasValue && UserId.HasValue && ownerId.Value == UserId.Value;
        return Task.FromResult(userOwned || IsInDivisionScope(divisionOwnerId));
    }

    public Task<bool> CanAccessInitiativePortfolioAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HasInitiativePortfolioAccess);

    public Task<bool> CanAccessInitiativeAsync(
        Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default)
    {
        if (!HasDivisionScope)
        {
            return Task.FromResult(HasInitiativePortfolioAccess);
        }

        var userOwned = ownerId.HasValue && UserId.HasValue && ownerId.Value == UserId.Value;
        return Task.FromResult(userOwned || IsInDivisionScope(divisionOwnerId));
    }

    public async Task EnsureInitiativeAsync(
        Guid? ownerId, Guid? divisionOwnerId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessInitiativeAsync(ownerId, divisionOwnerId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this initiative.");
        }
    }
}
