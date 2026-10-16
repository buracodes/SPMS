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
        var users = await userRepository.GetAll();
        var dtos = users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            FullName = u.FullName,
            IsActive = u.IsActive,
            LastLoginAtUtc = u.LastLoginAtUtc,
            Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList()
        }).ToList();
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
            Data = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                IsActive = user.IsActive,
                LastLoginAtUtc = user.LastLoginAtUtc,
                Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList()
            }
        });
    }

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
