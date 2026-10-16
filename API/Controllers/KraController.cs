using Application.CQRS.Kra.Requests.Commands;
using Application.CQRS.Kra.Requests.Queries;
using Application.DTOS.Kra;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class KraController : ControllerBase
{
    private readonly IMediator mediator;
    public KraController(IMediator _mediator)
    {
        mediator = _mediator;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.KraView)]
    public async Task<ActionResult<List<GetAllKraDto>>> GetAll(
        [FromQuery] Guid? scorecardId,
        [FromQuery] Guid? goalId,
        [FromQuery] Guid? fiscalYearId)
    {
        var result = await mediator.Send(new GetAllKraQueryRequest
        {
            ScorecardId = scorecardId,
            GoalId = goalId,
            FiscalYearId = fiscalYearId
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.KraView)]
    public async Task<ActionResult<KraDto>> GetById(Guid id)
    {
        var result = await mediator.Send(new GetKraByIdQueryRequest { Id = id });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.KraCreate)]
    public async Task<ActionResult> Create([FromBody] CreateKraDto dto)
    {
        var result = await mediator.Send(new CreateKraCommandRequest
        {
            GoalId = dto.GoalId,
            ScorecardId = dto.ScorecardId,
            Name = dto.Name,
            Description = dto.Description,
            Icon = dto.Icon,
            StrategicThemeId = dto.StrategicThemeId,
            UnitMeasure = dto.UnitMeasure,
            Weight = dto.Weight
        });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.KraEdit)]
    public async Task<ActionResult<KraDto>> Update(Guid id, [FromBody] UpdateKraCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.KraDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await mediator.Send(new DeleteKraCommandRequest { Id = id });
        return NoContent();
    }
}
