using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A direct, individual permission assignment (grant or deny) that applies to one user
/// independently of their group memberships (FR: "UserRight"). Direct denies are the
/// strongest statement in the central precedence policy; direct grants still cannot
/// bypass resource-scoped rules such as period locks and data ownership.
/// </summary>
public class UserRight : CommonEntity
{
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }

    public PermissionEffect Effect { get; set; } = PermissionEffect.Grant;

    /// <summary>
    /// The justification captured with the assignment for the authorization audit trail.
    /// </summary>
    public string? Reason { get; set; }

    public User User { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
