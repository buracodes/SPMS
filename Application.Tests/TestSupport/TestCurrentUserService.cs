using Application.Interface.IService;

namespace Application.Tests.TestSupport;

internal sealed class TestCurrentUserService : ICurrentUserService
{
    private Guid? divisionId;
    private IReadOnlyList<Guid> divisionIds = Array.Empty<Guid>();

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public bool IsAuthenticated => UserId.HasValue;
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    /// <summary>Primary division; tests may set it alone and the scope set follows automatically.</summary>
    public Guid? DivisionId
    {
        get => divisionId ?? (divisionIds.Count > 0 ? divisionIds[0] : null);
        set => divisionId = value;
    }

    /// <summary>All authorized divisions. Empty means the primary division is the only scope.</summary>
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
}
