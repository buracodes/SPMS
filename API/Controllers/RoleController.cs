using Application.Interface.IRepository;
using Application.Responses;
using Asp.Versioning;
using Domain.Constants;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class RoleController : ControllerBase
{
    private readonly IGenericRepository<Role> roleRepository;
    private readonly IGenericRepository<RolePermission> rolePermRepository;
    private readonly IGenericRepository<Permission> permRepository;

    public RoleController(
        IGenericRepository<Role> _roleRepository,
        IGenericRepository<RolePermission> _rolePermRepository,
        IGenericRepository<Permission> _permRepository)
    {
        roleRepository = _roleRepository;
        rolePermRepository = _rolePermRepository;
        permRepository = _permRepository;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.AdminRolesManage)]
    public async Task<ActionResult<Response<List<RoleDto>>>> GetAll()
    {
        var roles = await roleRepository.GetAll();
        var dtos = roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            UserCount = r.UserRoles.Count,
            PermissionCount = r.RolePermissions.Count
        }).ToList();
        return Ok(new Response<List<RoleDto>> { Success = true, Message = "Roles retrieved successfully.", Data = dtos });
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
