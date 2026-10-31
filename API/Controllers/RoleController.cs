using Application.CQRS.AccessControl;
using Application.CQRS.AccessControl.Requests.Commands;
using Application.CQRS.AccessControl.Requests.Queries;
using Application.Interface.IRepository;
using Application.Responses;
using Asp.Versioning;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Role management surface (legacy <c>admin:roles.manage</c> capability). The writes are
/// delegated to the same audited ADR-011 handlers used by the Access Control page, so a
/// role changed here behaves exactly like a "group" changed there — including the audit
/// trail and the live effective-permission evaluation.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class RoleController : ControllerBase
{
    private readonly IMediator mediator;
    private readonly IGenericRepository<Role> roleRepository;
    private readonly IGenericRepository<RolePermission> rolePermRepository;
    private readonly IGenericRepository<Permission> permRepository;

    public RoleController(
        IMediator _mediator,
        IGenericRepository<Role> _roleRepository,
        IGenericRepository<RolePermission> _rolePermRepository,
        IGenericRepository<Permission> _permRepository)
    {
        mediator = _mediator;
        roleRepository = _roleRepository;
        rolePermRepository = _rolePermRepository;
        permRepository = _permRepository;
    }

    // ── Reads ──────────────────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<List<AccessGroupDto>>>> GetAll()
    {
        var result = await mediator.Send(new GetAccessGroupsQueryRequest());
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<RoleDetailDto>>> GetById(Guid id)
    {
        var role = await roleRepository.GetById(id);
        if (role == null) return NotFound();

        var allPerms = await permRepository.GetAll();
        var assignedPermIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        return Ok(new Response<RoleDetailDto>
        {
            Success = true,
            Message = "Role retrieved successfully.",
            Data = new RoleDetailDto
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                UserCount = role.UserRoles.Count,
                Permissions = allPerms.Select(p => new PermissionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Module = p.Module,
                    Description = p.Description,
                    IsAssigned = assignedPermIds.Contains(p.Id)
                }).ToList()
            }
        });
    }

    [HttpGet("permissions")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<List<PermissionDto>>>> GetPermissions()
    {
        var perms = await permRepository.GetAll();
        var dtos = perms.Select(p => new PermissionDto
        {
            Id = p.Id,
            Name = p.Name,
            Module = p.Module,
            Description = p.Description,
            IsAssigned = false
        }).ToList();
        return Ok(new Response<List<PermissionDto>> { Success = true, Message = "Permissions retrieved successfully.", Data = dtos });
    }

    /// <summary>Rights catalog in the Access Control page's shape, including usage counts.</summary>
    [HttpGet("rights")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<List<AccessRightDto>>>> GetRights()
    {
        var result = await mediator.Send(new GetAccessRightsQueryRequest());
        return Ok(result);
    }

    [HttpGet("{id:guid}/rights")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<List<GroupRightRowDto>>>> GetRoleRights(Guid id)
    {
        var result = await mediator.Send(new GetGroupRightsQueryRequest { GroupId = id });
        return Ok(result);
    }

    // ── Writes (delegated to the audited access-control handlers) ───────────

    [HttpPost]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<AccessGroupDto>>> Create([FromBody] CreateRoleDto dto)
    {
        var result = await mediator.Send(new CreateAccessGroupCommandRequest
        {
            Dto = new AccessGroupWriteDto
            {
                Name = dto.Name,
                Description = dto.Description
            }
        });
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<AccessGroupDto>>> Update(Guid id, [FromBody] UpdateRoleDto dto)
    {
        var result = await mediator.Send(new UpdateAccessGroupCommandRequest
        {
            Id = id,
            Dto = new AccessGroupUpdateDto { Description = dto.Description }
        });
        return Ok(result);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<AccessGroupDto>>> SetStatus(Guid id, [FromBody] SetRoleStatusDto dto)
    {
        var result = await mediator.Send(new SetAccessGroupStatusCommandRequest
        {
            Id = id,
            Dto = new AccessGroupStatusDto { IsActive = dto.IsActive }
        });
        return Ok(result);
    }

    [HttpPut("{id:guid}/rights/{rightId:guid}")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<GroupRightRowDto>>> SetRight(
        Guid id, Guid rightId, [FromBody] SetRoleRightDto dto)
    {
        var result = await mediator.Send(new SetGroupRightCommandRequest
        {
            GroupId = id,
            RightId = rightId,
            Effect = dto.Effect
        });
        return Ok(result);
    }

    [HttpDelete("{id:guid}/rights/{rightId:guid}")]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<bool>>> RemoveRight(
        Guid id, Guid rightId, [FromBody] RemoveRoleRightDto? dto)
    {
        var result = await mediator.Send(new RemoveGroupRightCommandRequest
        {
            GroupId = id,
            RightId = rightId,
            Reason = dto?.Reason
        });
        return Ok(result);
    }
}

public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateRoleDto
{
    public string? Description { get; set; }
}

public class SetRoleStatusDto
{
    public bool IsActive { get; set; }
}

public class SetRoleRightDto
{
    public PermissionEffect Effect { get; set; } = PermissionEffect.Grant;
}

public class RemoveRoleRightDto
{
    public string? Reason { get; set; }
}

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int UserCount { get; set; }
    public int PermissionCount { get; set; }
}

public class RoleDetailDto : RoleDto
{
    public List<PermissionDto> Permissions { get; set; } = new();
}

public class PermissionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsAssigned { get; set; }
}
