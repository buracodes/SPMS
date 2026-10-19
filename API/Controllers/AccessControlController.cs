using Application.CQRS.AccessControl.Requests.Commands;
using Application.CQRS.AccessControl.Requests.Queries;
using Application.Interface.IService;
using Application.Responses;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace API.Controllers;

/// <summary>
/// Access Control Management (dynamic RBAC, ADR-011). Page reads sit behind
/// admin:access.view; the three manage capabilities each guard their own slice:
///   - admin:access.groups.manage  → groups, memberships, group rights
///   - admin:access.rights.manage  → the rights catalog
///   - admin:access.user-rights.manage → direct user grants & denies
/// An Admin group with all four sees everything; a Power-User group seeded with
/// access.view + access.groups.manage sees the read tab plus the groups slice only.
/// Precedence and all effective-access decisions are computed exclusively by the
/// backend (IEffectivePermissionService) — this controller never trusts counts
/// from the client.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class AccessControlController : ControllerBase
{
    private readonly IMediator mediator;
    private readonly IEffectivePermissionService effectivePermissions;

    public AccessControlController(IMediator _mediator, IEffectivePermissionService _effectivePermissions)
    {
        mediator = _mediator;
        effectivePermissions = _effectivePermissions;
    }

    // ── Reads ──────────────────────────────────────────────────────────────

    [HttpGet("summary")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessSummaryDto>>> GetSummary()
    {
        var result = await mediator.Send(new GetAccessSummaryQueryRequest());
        return Ok(result);
    }

    [HttpGet("groups")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.AccessGroupDto>>>> GetGroups()
    {
        var result = await mediator.Send(new GetAccessGroupsQueryRequest());
        return Ok(result);
    }

    [HttpGet("groups/{id:guid}/rights")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.GroupRightRowDto>>>> GetGroupRights(Guid id)
    {
        var result = await mediator.Send(new GetGroupRightsQueryRequest { GroupId = id });
        return Ok(result);
    }

    [HttpGet("memberships")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.GroupMembershipRowDto>>>> GetMemberships(
        [FromQuery] Guid? groupId)
    {
        var result = await mediator.Send(new GetGroupMembershipQueryRequest { GroupId = groupId });
        return Ok(result);
    }

    [HttpGet("rights")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.AccessRightDto>>>> GetRights()
    {
        var result = await mediator.Send(new GetAccessRightsQueryRequest());
        return Ok(result);
    }

    [HttpGet("directory")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.AccessDirectoryUserDto>>>> GetDirectory()
    {
        var result = await mediator.Send(new GetAccessDirectoryQueryRequest());
        return Ok(result);
    }

    [HttpGet("directory/{id:guid}")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessUserDetailDto>>> GetDirectoryUser(Guid id)
    {
        var result = await mediator.Send(new GetAccessUserDetailQueryRequest { UserId = id });
        return Ok(result);
    }

    [HttpGet("directory/{id:guid}/rights")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.UserRightRowDto>>>> GetUserRights(Guid id)
    {
        var result = await mediator.Send(new GetUserRightsQueryRequest { UserId = id });
        return Ok(result);
    }

    [HttpGet("directory/{id:guid}/effective-access")]
    [Authorize(Policy = Permissions.AdminAccessView)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.EffectiveAccessResultDto>>> GetEffectiveAccess(Guid id)
    {
        var result = await mediator.Send(new GetEffectiveAccessQueryRequest { UserId = id });
        return Ok(result);
    }

    [HttpGet("audit")]
    [Authorize(Policy = Permissions.AdminAuditView)]
    public async Task<ActionResult<Response<List<Application.CQRS.AccessControl.AuthorizationAuditEntryDto>>>> GetAuthorizationAudit()
    {
        var result = await mediator.Send(new GetAuthorizationAuditQueryRequest());
        return Ok(result);
    }

    // ── Groups (groups capability) ─────────────────────────────────────────

    [HttpPost("groups")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessGroupDto>>> CreateGroup(
        [FromBody] CreateAccessGroupCommandRequest command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPut("groups/{id:guid}")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessGroupDto>>> UpdateGroup(
        Guid id, [FromBody] UpdateAccessGroupCommandRequest command)
    {
        command.Id = id;
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPut("groups/{id:guid}/status")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessGroupDto>>> SetGroupStatus(
        Guid id, [FromBody] SetAccessGroupStatusCommandRequest command)
    {
        command.Id = id;
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("groups/{id:guid}/members")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.GroupMembershipRowDto>>> AssignMember(
        Guid id, [FromBody] AssignGroupMembershipCommandRequest command)
    {
        command.GroupId = id;
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("groups/{id:guid}/members/{userId:guid}")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<bool>>> RemoveMember(
        Guid id, Guid userId, [FromBody] RemoveGroupMembershipDto? body)
    {
        var result = await mediator.Send(new RemoveGroupMembershipCommandRequest
        {
            GroupId = id,
            UserId = userId,
            Reason = body?.Reason
        });
        return Ok(result);
    }

    [HttpPut("groups/{id:guid}/rights/{rightId:guid}")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.GroupRightRowDto>>> SetGroupRight(
        Guid id, Guid rightId, [FromBody] SetGroupRightDto body)
    {
        var result = await mediator.Send(new SetGroupRightCommandRequest
        {
            GroupId = id,
            RightId = rightId,
            Effect = body.Effect
        });
        return Ok(result);
    }

    [HttpDelete("groups/{id:guid}/rights/{rightId:guid}")]
    [Authorize(Policy = Permissions.AdminAccessGroupsManage)]
    public async Task<ActionResult<Response<bool>>> RemoveGroupRight(
        Guid id, Guid rightId, [FromBody] RemoveGroupRightDto? body)
    {
        var result = await mediator.Send(new RemoveGroupRightCommandRequest
        {
            GroupId = id,
            RightId = rightId,
            Reason = body?.Reason
        });
        return Ok(result);
    }

    // ── Rights catalog (rights capability) ─────────────────────────────────

    [HttpPost("rights")]
    [Authorize(Policy = Permissions.AdminRightsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessRightDto>>> CreateRight(
        [FromBody] CreateAccessRightCommandRequest command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPut("rights/{id:guid}")]
    [Authorize(Policy = Permissions.AdminRightsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.AccessRightDto>>> UpdateRight(
        Guid id, [FromBody] UpdateAccessRightCommandRequest command)
    {
        command.Id = id;
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("rights/{id:guid}")]
    [Authorize(Policy = Permissions.AdminRightsManage)]
    public async Task<ActionResult<Response<bool>>> DeleteRight(Guid id, [FromBody] DeleteAccessRightDto? body)
    {
        var result = await mediator.Send(new DeleteAccessRightCommandRequest
        {
            Id = id,
            Reason = body?.Reason
        });
        return Ok(result);
    }

    // ── Direct user rights (user-rights capability) ────────────────────────

    [HttpPut("directory/{id:guid}/rights/{rightId:guid}")]
    [Authorize(Policy = Permissions.AdminUserRightsManage)]
    public async Task<ActionResult<Response<Application.CQRS.AccessControl.UserRightRowDto>>> SetUserRight(
        Guid id, Guid rightId, [FromBody] SetUserRightDto body)
    {
        var result = await mediator.Send(new SetUserRightCommandRequest
        {
            UserId = id,
            RightId = rightId,
            Effect = body.Effect,
            Reason = body?.Reason
        });
        return Ok(result);
    }

    [HttpDelete("directory/{id:guid}/rights/{rightId:guid}")]
    [Authorize(Policy = Permissions.AdminUserRightsManage)]
    public async Task<ActionResult<Response<bool>>> RemoveUserRight(
        Guid id, Guid rightId, [FromBody] RemoveUserRightDto? body)
    {
        var result = await mediator.Send(new RemoveUserRightCommandRequest
        {
            UserId = id,
            RightId = rightId,
            Reason = body?.Reason
        });
        return Ok(result);
    }
}

public class RemoveGroupMembershipDto
{
    public string? Reason { get; set; }
}

public class SetGroupRightDto
{
    public Domain.Enums.PermissionEffect Effect { get; set; } = Domain.Enums.PermissionEffect.Grant;
}

public class RemoveGroupRightDto
{
    public string? Reason { get; set; }
}

public class SetUserRightDto
{
    public Domain.Enums.PermissionEffect Effect { get; set; } = Domain.Enums.PermissionEffect.Grant;
    public string? Reason { get; set; }
}

public class RemoveUserRightDto
{
    public string? Reason { get; set; }
}

public class DeleteAccessRightDto
{
    public string? Reason { get; set; }
}
