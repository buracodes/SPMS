using Application.Interface.IService;

namespace Application.Tests.TestSupport;

internal sealed class TestCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public bool IsAuthenticated => UserId.HasValue;
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public Guid? DivisionId { get; set; }
}
