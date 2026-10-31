using System;

namespace Application.CQRS.AccessControl;

/// <summary>How the final effective result for one right was reached (ADR-011 precedence).</summary>
public enum AccessResult : byte
{
    DefaultDeny = 0,
    Granted = 1,
    Denied = 2
}

/// <summary>
/// Where the decision came from. <see cref="AccessResult.DefaultDeny"/> entries carry
/// <see cref="AccessSource.DefaultDeny"/> — no grant exists anywhere in the policy.
/// </summary>
public enum AccessSource : byte
{
    None = 0,
    DirectUserAssignment = 1,
    GroupAssignment = 2,
    PositionAssignment = 3
}

/// <summary>Collection counts shown on the Access Control summary cards.</summary>
public record AccessSummaryDto(
    int TotalGroups,
    int TotalRights,
    int GroupMembershipAssignments,
    int DirectGrantAssignments,
    int DirectDenyAssignments);

/// <summary>One row of the Groups table.</summary>
public record AccessGroupDto(
    Guid Id,
    string Name,
    string? Description,
    int MemberCount,
    int RightCount,
    int DenyCount,
    bool IsActive,
    string Status,
    DateTime? LastUpdated);

/// <summary>One row of the Rights catalog.</summary>
public record AccessRightDto(
    Guid Id,
    string Name,
    string Module,
    string? Description,
    int GroupAssignments,
    int DirectAssignments,
    bool Referenced,
    string Status,
    DateTime? LastUpdated);

/// <summary>One row of the employee directory used by the member/user-rights selectors.</summary>
public record AccessDirectoryUserDto(
    Guid Id,
    string Username,
    string? Email,
    string FullName,
    bool IsActive,
    Guid? DivisionId,
    string? DivisionName,
    string[] GroupNames);

/// <summary>Employee detail shown in the user-rights / effective access workflow.</summary>
public record AccessUserDetailDto(
    Guid Id,
    string Username,
    string? Email,
    string FullName,
    bool IsActive,
    Guid? DivisionId,
    string? DivisionName,
    string[] GroupNames,
    int UnassignedGroupsCount);

/// <summary>An assignment linking a user to one access group (membership).</summary>
public record GroupMembershipRowDto(
    Guid UserId,
    Guid RoleId,
    string Username,
    string FullName,
    string RoleName,
    DateTime AssignedAt,
    string? AssignedByName);

/// <summary>An assignment linking one right to one group with an explicit effect.</summary>
public record GroupRightRowDto(
    Guid RoleId,
    Guid PermissionId,
    string RoleName,
    string PermissionName,
    string Module,
    string Effect,
    DateTime AssignedAt,
    string? AssignedByName);

/// <summary>An assignment linking one right directly to one user.</summary>
public record UserRightRowDto(
    Guid UserId,
    Guid PermissionId,
    string Username,
    string FullName,
    string PermissionName,
    string Module,
    string Effect,
    string? Reason,
    DateTime AssignedAt,
    string? AssignedByName);

/// <summary>Effective-access explanation for one right of one user (backend-computed, ADR-011).</summary>
public record EffectiveAccessEntryDto(
    Guid PermissionId,
    string PermissionName,
    string Module,
    string? Description,
    string Result,
    string Source,
    bool HasExplicitDeny,
    string Explanation);

/// <summary>The complete backend-computed authorization picture of one user.</summary>
public record EffectiveAccessResultDto(
    Guid UserId,
    string Username,
    string FullName,
    bool IsActive,
    string[] GroupNames,
    EffectiveAccessEntryDto[] Rights);

/// <summary>A row of the Authorization Audit tab (append-only, backend generated).</summary>
public record AuthorizationAuditEntryDto(
    Guid Id,
    string EntityType,
    string EntityId,
    string Action,
    string? PerformedByName,
    DateTime PerformedAt,
    string? Reason,
    string? OldValues,
    string? NewValues);
