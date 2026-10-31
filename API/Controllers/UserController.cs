using Application.DTOS.User;
using Application.Interface.IRepository;
using Application.Responses;
using Application.Services;
using Asp.Versioning;
using Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class UserController : ControllerBase
{
    private readonly IUserRepository userRepository;
    private readonly IHRInfoRepository hrInfoRepository;

    public UserController(IUserRepository _userRepository, IHRInfoRepository _hrInfoRepository)
    {
        userRepository = _userRepository;
        hrInfoRepository = _hrInfoRepository;
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
    /// Active employees available as KPI owners, sourced from HR so an employee can own a KPI
    /// before their first SPMS sign-in. EmpId is the AD objectGUID and therefore the stable local
    /// user id. Deliberately readable with only kpi:view so planners who cannot administer users
    /// can still pick an owner.
    /// </summary>
    [HttpGet("lookup")]
    [Authorize(Policy = Permissions.KpiView)]
    public async Task<ActionResult<Response<List<UserDto>>>> Lookup(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var employees = await hrInfoRepository.GetActiveEmployeesAsync(search, 1000, cancellationToken);
        var dtos = employees
            .Where(employee => employee.EmpId.HasValue)
            .Select(employee => new UserDto
            {
                Id = employee.EmpId!.Value,
                Username = EmployeeNaming.UsernameHint(employee),
                Email = employee.Email,
                FullName = EmployeeNaming.DisplayName(employee),
                IsActive = true,
                DivisionId = employee.DivisionId,
                DivisionName = employee.DivisionName,
                Roles = new List<string>()
            })
            .ToList();

        return Ok(new Response<List<UserDto>>
        {
            Success = true,
            Message = "Employees retrieved successfully.",
            Data = dtos
        });
    }

    private static UserDto ToDto(Domain.Entities.User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Email = u.Email,
        FullName = u.FullName,
        IsActive = u.IsActive,
        DivisionId = u.DivisionId,
        DivisionName = u.Division?.Name,
        Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList()
    };
}
