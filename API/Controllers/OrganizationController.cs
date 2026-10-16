using Application.CQRS.Organization.Requests;
using Application.DTOS.Organization;
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
public class OrganizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.OrganizationView)]
    public async Task<ActionResult<Response<OrganizationProfileDto>>> GetProfile()
    {
        var result = await _mediator.Send(new GetOrganizationProfileRequest());
        return Ok(result);
    }

    [HttpPut("identity")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<OrganizationProfileDto>>> UpdateIdentity([FromBody] UpdateOrganizationIdentityDto dto)
    {
        var result = await _mediator.Send(new UpdateOrganizationIdentityCommand { Dto = dto });
        return Ok(result);
    }

    // Core Values
    [HttpPost("core-values")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<CoreValueDto>>> CreateCoreValue([FromBody] CreateCoreValueDto dto)
    {
        var result = await _mediator.Send(new CreateCoreValueCommand { Dto = dto });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("core-values/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<CoreValueDto>>> UpdateCoreValue(Guid id, [FromBody] UpdateCoreValueDto dto)
    {
        dto.Id = id;
        var result = await _mediator.Send(new UpdateCoreValueCommand { Dto = dto });
        return Ok(result);
    }

    [HttpDelete("core-values/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<bool>>> DeleteCoreValue(Guid id)
    {
        var result = await _mediator.Send(new DeleteCoreValueCommand { Id = id });
        return Ok(result);
    }

    // Strategic Themes
    [HttpPost("strategic-themes")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<StrategicThemeDto>>> CreateStrategicTheme([FromBody] CreateStrategicThemeDto dto)
    {
        var result = await _mediator.Send(new CreateStrategicThemeCommand { Dto = dto });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("strategic-themes/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<StrategicThemeDto>>> UpdateStrategicTheme(Guid id, [FromBody] UpdateStrategicThemeDto dto)
    {
        dto.Id = id;
        var result = await _mediator.Send(new UpdateStrategicThemeCommand { Dto = dto });
        return Ok(result);
    }

    [HttpDelete("strategic-themes/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<bool>>> DeleteStrategicTheme(Guid id)
    {
        var result = await _mediator.Send(new DeleteStrategicThemeCommand { Id = id });
        return Ok(result);
    }

    // SWOT
    [HttpPost("swot")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<SwotItemDto>>> CreateSwotItem([FromBody] CreateSwotItemDto dto)
    {
        var result = await _mediator.Send(new CreateSwotItemCommand { Dto = dto });
        return StatusCode(StatusCodes.Status201Created, result);
    }
    [HttpGet("swot")]
    [Authorize(Policy = Permissions.OrganizationView)]
    public async Task<ActionResult<Response<List<SwotItemDto>>>> GetSwotItems()
    {
        var result = await _mediator.Send(new GetSwotItemsRequest());
        return Ok(result);
    }

    [HttpPut("swot/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<SwotItemDto>>> UpdateSwotItem(Guid id, [FromBody] UpdateSwotItemDto dto)
    {
        dto.Id = id;
        var result = await _mediator.Send(new UpdateSwotItemCommand { Dto = dto });
        return Ok(result);
    }

    [HttpDelete("swot/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<bool>>> DeleteSwotItem(Guid id)
    {
        var result = await _mediator.Send(new DeleteSwotItemCommand { Id = id });
        return Ok(result);
    }

    // PESTEL
    [HttpPost("pestel")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<PestelItemDto>>> CreatePestelItem([FromBody] CreatePestelItemDto dto)
    {
        var result = await _mediator.Send(new CreatePestelItemCommand { Dto = dto });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("pestel/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<PestelItemDto>>> UpdatePestelItem(Guid id, [FromBody] UpdatePestelItemDto dto)
    {
        dto.Id = id;
        var result = await _mediator.Send(new UpdatePestelItemCommand { Dto = dto });
        return Ok(result);
    }

    [HttpDelete("pestel/{id}")]
    [Authorize(Policy = Permissions.OrganizationEdit)]
    public async Task<ActionResult<Response<bool>>> DeletePestelItem(Guid id)
    {
        var result = await _mediator.Send(new DeletePestelItemCommand { Id = id });
        return Ok(result);
    }
}
