using Application.CQRS.Goals.Requests.Commands;
using Application.CQRS.Goals.Requests.Queries;
using Application.DTOS.Goal;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class GoalController : ControllerBase
{
    private readonly IMediator mediator;
    public GoalController(IMediator _mediator)
    {
        mediator = _mediator;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.GoalsView)]
    public async Task<ActionResult<List<GetAllGoalDto>>> GetAll([FromQuery] Guid? scorecardId, [FromQuery] Guid? fiscalYearId)
    {
        var result = await mediator.Send(new GetAllGoalRequest { ScorecardId = scorecardId, FiscalYearId = fiscalYearId });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.GoalsView)]
    public async Task<ActionResult<GoalDto>> GetById(Guid id)
    {
        var result = await mediator.Send(new GetGoalByIdQueryRequest { Id = id });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.GoalsCreate)]
    public async Task<ActionResult> Create([FromBody] CreateGoalDto dto)
    {
        var result = await mediator.Send(new CreateGoalCommandRequest { Dto = dto });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.GoalsEdit)]
    public async Task<ActionResult<GoalDto>> Update(Guid id, [FromBody] UpdateGoalCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.GoalsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await mediator.Send(new DeleteGoalCommandRequest { Id = id });
        return NoContent();
    }
}
