using Application.CQRS.AccessControl;
using Application.Responses;
using Domain.Enums;
using MediatR;
using System;

namespace Application.CQRS.AccessControl.Requests.Commands;

public class CreateAccessGroupCommandRequest : IRequest<Response<AccessGroupDto>>
{
    public required AccessGroupWriteDto Dto { get; set; }
}

public class UpdateAccessGroupCommandRequest : IRequest<Response<AccessGroupDto>>
{
    public Guid Id { get; set; }
    public required AccessGroupUpdateDto Dto { get; set; }
}

public class SetAccessGroupStatusCommandRequest : IRequest<Response<AccessGroupDto>>
{
    public Guid Id { get; set; }
    public required AccessGroupStatusDto Dto { get; set; }
}

public class AccessGroupWriteDto
{
    /// <summary>Immutable technical key of the access group — matches Role.Name.</summary>
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AccessGroupUpdateDto
{
    public string? Description { get; set; }
}

public class AccessGroupStatusDto
{
    public bool IsActive { get; set; }
}

public class AssignGroupMembershipCommandRequest : IRequest<Response<GroupMembershipRowDto>>
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
}

public class RemoveGroupMembershipCommandRequest : IRequest<Response<bool>>
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Audit explanation the persistence interceptor appends (required for justification).</summary>
    public string? Reason { get; set; }
}

public class SetGroupRightCommandRequest : IRequest<Response<GroupRightRowDto>>
{
    public Guid GroupId { get; set; }
    public Guid RightId { get; set; }
    /// <summary>"Grant" or "Deny" (PermissionEffect).</summary>
    public required PermissionEffect Effect { get; set; }
}

public class RemoveGroupRightCommandRequest : IRequest<Response<bool>>
{
    public Guid GroupId { get; set; }
    public Guid RightId { get; set; }
    public string? Reason { get; set; }
}

public class SetUserRightCommandRequest : IRequest<Response<UserRightRowDto>>
{
    public Guid UserId { get; set; }
    public Guid RightId { get; set; }
    public required PermissionEffect Effect { get; set; }
    /// <summary>Required justification for every direct user right (FR: reason on UserRight).</summary>
    public string? Reason { get; set; }
}

public class RemoveUserRightCommandRequest : IRequest<Response<bool>>
{
    public Guid UserId { get; set; }
    public Guid RightId { get; set; }
    public string? Reason { get; set; }
}

public class CreateAccessRightCommandRequest : IRequest<Response<AccessRightDto>>
{
    public required AccessRightWriteDto Dto { get; set; }
}

public class UpdateAccessRightCommandRequest : IRequest<Response<AccessRightDto>>
{
    public Guid Id { get; set; }
    public required AccessRightUpdateDto Dto { get; set; }
}

public class DeleteAccessRightCommandRequest : IRequest<Response<bool>>
{
    public Guid Id { get; set; }
    public string? Reason { get; set; }
}

public class AccessRightWriteDto
{
    /// <summary>Catalog key "module:resource[.action]" — case-insensitive, immutable after create.</summary>
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AccessRightUpdateDto
{
    public string? Module { get; set; }
    public string? Description { get; set; }
}
