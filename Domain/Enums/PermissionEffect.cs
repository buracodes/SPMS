namespace Domain.Enums;

/// <summary>
/// The explicit effect of a dynamic RBAC assignment. Assigned to rights that are granted
/// to group members or to individual users. Deny wins over Grant under the central
/// precedence policy (ADR-011), and "no applicable grant" is treated as denied even
/// without an explicit deny row.
/// </summary>
public enum PermissionEffect : int
{
    Grant = 1,
    Deny = 2
}
