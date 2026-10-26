using Application.CQRS.Auth.Requests.Commands;
using Application.DTOS.User;
using Application.Interface.IRepository;
using Application.Responses;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class UserController : ControllerBase
{
    private readonly IUserRepository userRepository;
    private readonly IMediator mediator;

    public UserController(IUserRepository _userRepository, IMediator _mediator)
    {
        userRepository = _userRepository;
        mediator = _mediator;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.AdminUsersManage)]
    public async Task<ActionResult<Response<List<UserDto>>>> GetAll()
    {
        var users = await userRepository.GetAllWithRolesAndDivisionAsync();
        var dtos = users.Select(ToDto).ToList();
        return Ok(new Response<List<UserDto>> { Success = true, Message = "Users retrieved successfully.", Data = dtos });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.AdminUsersManage)]
    public async Task<ActionResult<Response<UserDto>>> GetById(Guid id)
    {
        var user = await userRepository.GetByIdWithRolesAndPermissionsAsync(id);
        if (user == null) return NotFound();

        return Ok(new Response<UserDto>
        {
            Success = true,
            Message = "User retrieved successfully.",
            Data = ToDto(user)
        });
    }

    /// <summary>
    /// Active users available as KPI owners. Deliberately readable with only kpi:view so
    /// the KPI owner picker works for planners who cannot administer users.
    /// </summary>
    [HttpGet("lookup")]
    [Authorize(Policy = Permissions.KpiView)]
    public async Task<ActionResult<Response<List<UserDto>>>> Lookup()
    {
        var users = await userRepository.GetAllWithRolesAndDivisionAsync();
        var dtos = users.Where(u => u.IsActive).Select(ToDto).ToList();
        return Ok(new Response<List<UserDto>> { Success = true, Message = "Users retrieved successfully.", Data = dtos });
    }

    private static UserDto ToDto(Domain.Entities.User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Email = u.Email,
        FullName = u.FullName,
        IsActive = u.IsActive,
        LastLoginAtUtc = u.LastLoginAtUtc,
        DivisionId = u.DivisionId,
        DivisionName = u.Division?.Name,
        Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList()
    };

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<Response<bool>>> ChangePassword([FromBody] ChangePasswordCommandRequest request)
    {
        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpPost("divisions/reset-password")]
    [Authorize(Policy = Permissions.AdminUsersManage)]
    public async Task<ActionResult<Response<bool>>> ResetDivisionPassword([FromBody] ResetDivisionPasswordCommandRequest request)
    {
        var result = await mediator.Send(request);
        return Ok(result);
    }
}
