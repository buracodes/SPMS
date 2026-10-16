using Application.CQRS.Kpi.Requests.Commands;
using Application.CQRS.Kpi.Requests.Queries;
using Application.DTOS.Kpi;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class KpiController : ControllerBase
{
    private readonly IMediator mediator;
    public KpiController(IMediator _mediator)
    {
        mediator = _mediator;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.KpiView)]
    public async Task<ActionResult<List<GetAllKpiDto>>> GetAll(
        [FromQuery] Guid? scorecardId,
        [FromQuery] Guid? kraId,
        [FromQuery] Guid? goalId,
        [FromQuery] Guid? fiscalYearId)
    {
        var result = await mediator.Send(new GetAllKpiQueryRequest
        {
            ScorecardId = scorecardId,
            KraId = kraId,
            GoalId = goalId,
            FiscalYearId = fiscalYearId
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.KpiView)]
    public async Task<ActionResult<KpiDto>> GetById(Guid id)
    {
        var result = await mediator.Send(new GetKpiByIdQueryRequest { Id = id });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.KpiCreate)]
    public async Task<ActionResult> Create([FromBody] CreateKpiDto dto)
    {
        var result = await mediator.Send(new CreateKpiCommandRequest
        {
            ScorecardId = dto.ScorecardId,
            KraId = dto.KraId,
            DivisionGoalId = dto.DivisionGoalId,
            OwnerId = dto.OwnerId,
            DivisionOwnerId = dto.DivisionOwnerId,
            Name = dto.Name,
            Description = dto.Description,
            Unit = dto.Unit,
            Weight = dto.Weight,
            OptimizationType = dto.OptimizationType,
            DataSource = dto.DataSource,
            KpiType = dto.KpiType,
            IsCumulative = dto.IsCumulative,
            ParentKpiId = dto.ParentKpiId,
            Baseline = dto.Baseline,
            MinValue = dto.MinValue,
            MaxValue = dto.MaxValue,
            Formula = dto.Formula
        });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.KpiEdit)]
    public async Task<ActionResult<KpiDto>> Update(Guid id, [FromBody] UpdateKpiCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.KpiDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await mediator.Send(new DeleteKpiCommandRequest { Id = id });
        return NoContent();
    }
}
