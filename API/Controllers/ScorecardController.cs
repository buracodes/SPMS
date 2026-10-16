using Application.CQRS.Scorecard.Requests.Commands;
using Application.CQRS.Scorecard.Requests.Queries;
using Application.DTOS.Scorecard;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class ScorecardController : ControllerBase
{
    private readonly IMediator mediator;

    public ScorecardController(IMediator _mediator)
    {
        mediator = _mediator;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ScorecardView)]
    public async Task<ActionResult<List<GetAllScorecardDto>>> GetAll([FromQuery] Guid? fiscalYearId)
    {
        var result = await mediator.Send(new GetAllScorecardQueryRequest { FiscalYearId = fiscalYearId });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.ScorecardView)]
    public async Task<ActionResult<ScorecardDto>> GetById(Guid id)
    {
        var result = await mediator.Send(new GetScorecardByIdQueryRequest { Id = id });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ScorecardCreate)]
    public async Task<ActionResult<ScorecardDto>> Create([FromBody] CreateScorecardCommandRequest request)
    {
        var result = await mediator.Send(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.ScorecardEdit)]
    public async Task<ActionResult<ScorecardDto>> Update(Guid id, [FromBody] UpdateScorecardCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.ScorecardDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await mediator.Send(new DeleteScorecardCommandRequest { Id = id });
        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Permissions.ScorecardClose)]
    public async Task<ActionResult<ScorecardDto>> Close(Guid id)
    {
        var result = await mediator.Send(new CloseScorecardCommandRequest { Id = id });
        return Ok(result);
    }

    [HttpPost("clone")]
    [Authorize(Policy = Permissions.ScorecardClone)]
    public async Task<ActionResult<CloneScorecardResultDto>> Clone([FromBody] CloneScorecardCommandRequest request)
    {
        var result = await mediator.Send(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
