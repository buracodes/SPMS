using Application.CQRS.Initiative.Requests.Commands;
using Application.CQRS.Initiative.Requests.Queries;
using Application.DTOS.Initiative;
using Application.Exceptions;
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
public class InitiativeController : ControllerBase
{
    private readonly IMediator mediator;

    public InitiativeController(IMediator _mediator)
    {
        mediator = _mediator;
    }

    // Milestone, progress, dependency, expenditure and closure endpoints are gated on the
    // InitiativesView policy, not on the fine-grained permissions. InitiativeGuard applies the real
    // rule for those actions - the permission, the edit permission, or ownership of that specific
    // initiative - and returns a descriptive 403. Gating them on the permission here instead made
    // the guard's owner allowance unreachable: an owner holding only view was rejected by the
    // policy before the handler ran, and the UI (which mirrors the guard) showed no controls.
    //
    // The exceptions are Update (initiative details), Create and Delete, which stay on their
    // fine-grained policies: owners are never authorised for those.

    /// <summary>
    /// Initiative grid. Supports server-side search, filtering and paging, and returns the
    /// backend-calculated progress and budget figures the grid displays.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<PagedInitiativeDto>>> GetAll([FromQuery] GetAllInitiativeQuery query)
    {
        var result = await mediator.Send(query);
        return Ok(new Response<PagedInitiativeDto>
        {
            Success = true,
            Message = "Initiatives retrieved successfully.",
            Data = result
        });
    }

    /// <summary>Portfolio metrics for dashboards and reports.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeSummaryDto>>> GetSummary([FromQuery] GetInitiativeSummaryQuery query)
    {
        var result = await mediator.Send(query);
        return Ok(new Response<InitiativeSummaryDto>
        {
            Success = true,
            Message = "Initiative summary retrieved successfully.",
            Data = result
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> GetById(Guid id)
    {
        var result = await mediator.Send(new GetInitiativeByIdQueryRequest { Id = id });
        return Ok(result);
    }

    /// <summary>FR-403 progress/change history, newest first.</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<List<InitiativeUpdateLogDto>>>> GetHistory(Guid id, [FromQuery] Guid? milestoneId)
    {
        var result = await mediator.Send(new GetInitiativeHistoryQueryRequest
        {
            InitiativeId = id,
            MilestoneId = milestoneId
        });

        return Ok(new Response<List<InitiativeUpdateLogDto>>
        {
            Success = true,
            Message = "Initiative history retrieved successfully.",
            Data = result
        });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.InitiativesCreate)]
    public async Task<ActionResult<Response<InitiativeDto>>> Create([FromBody] CreateInitiativeCommandRequest request)
    {
        var result = await mediator.Send(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Initiative details: name, description, owner, priority, status, dates, budget plan and
    /// strategic links. Deliberately left on the edit policy — initiative owners are authorised
    /// for progress, expenditure and closure only, never for these fields (FRD responsibility
    /// matrix), so they must not be able to reach this action.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.InitiativesEdit)]
    public async Task<ActionResult<Response<InitiativeDto>>> Update(Guid id, [FromBody] UpdateInitiativeCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    /// <summary>
    /// FR-404 actual expenditure only. Gated on the view policy so the assigned owner can post
    /// spend against their own initiative; InitiativeGuard.EnsureCanUpdateExpenditure applies the
    /// real rule (the expenditure permission, the Power User correction permissions, or ownership).
    /// Nothing but the spend figure can be sent here, so reaching it never unlocks the detail fields
    /// that the edit policy above protects.
    /// </summary>
    [HttpPut("{id:guid}/expenditure")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> UpdateExpenditure(Guid id, [FromBody] UpdateInitiativeExpenditureCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.InitiativesDelete)]
    public async Task<ActionResult<Response<object>>> Delete(Guid id)
    {
        var result = await mediator.Send(new DeleteInitiativeCommandRequest { Id = id });
        return Ok(result);
    }

    [HttpGet("{initiativeId:guid}/milestones")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<List<MilestoneDto>>>> GetMilestones(Guid initiativeId)
    {
        var result = await mediator.Send(new GetMilestonesByInitiativeQueryRequest { InitiativeId = initiativeId });
        return Ok(result);
    }

    [HttpPost("{initiativeId:guid}/milestones")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<MilestoneDto>>> CreateMilestone(Guid initiativeId, [FromBody] CreateMilestoneCommandRequest request)
    {
        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{initiativeId:guid}/milestones/{id:guid}")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<MilestoneDto>>> UpdateMilestone(Guid initiativeId, Guid id, [FromBody] UpdateMilestoneCommandRequest request)
    {
        if (request.Id == Guid.Empty)
            request.Id = id;
        else if (request.Id != id)
            return BadRequest("ID mismatch.");

        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{initiativeId:guid}/milestones/{id:guid}")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<IActionResult> DeleteMilestone(Guid initiativeId, Guid id)
    {
        await mediator.Send(new DeleteMilestoneCommandRequest { Id = id, InitiativeId = initiativeId });
        return NoContent();
    }

    /// <summary>
    /// FR-403 progress update for a single milestone. Returns the recalculated initiative so the
    /// client never has to compute overall progress itself.
    /// </summary>
    [HttpPost("{initiativeId:guid}/milestones/{id:guid}/progress")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> UpdateMilestoneProgress(
        Guid initiativeId,
        Guid id,
        [FromBody] UpdateMilestoneProgressDto dto)
    {
        if (dto == null)
            throw new BadRequestException("Progress update data is required.");

        if (dto.InitiativeId == Guid.Empty)
            dto.InitiativeId = initiativeId;
        else if (dto.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        if (dto.MilestoneId == Guid.Empty)
            dto.MilestoneId = id;
        else if (dto.MilestoneId != id)
            return BadRequest("Milestone ID mismatch.");

        var result = await mediator.Send(new UpdateMilestoneProgressCommandRequest { Dto = dto });
        return Ok(result);
    }

    /// <summary>
    /// FR-405 closure. Locks the initiative once closure information is recorded.
    /// Gated on the view policy so InitiativeGuard.EnsureCanClose can apply the real rule: the
    /// close permission or ownership of this initiative. The fine-grained close policy here would
    /// reject an owner holding only view before the handler ran, even though the FRD makes the
    /// assigned owner an authorised closer.
    /// </summary>
    [HttpPost("{initiativeId:guid}/close")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> Close(Guid initiativeId, [FromBody] CloseInitiativeDto dto)
    {
        if (dto == null)
            throw new BadRequestException("Closure information is required.");

        if (dto.InitiativeId == Guid.Empty)
            dto.InitiativeId = initiativeId;
        else if (dto.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(new CloseInitiativeCommandRequest { Dto = dto });
        return Ok(result);
    }

    /// <summary>Reopens a closed initiative for correction. Requires the close permission.</summary>
    [HttpPost("{initiativeId:guid}/reopen")]
    [Authorize(Policy = Permissions.InitiativesClose)]
    public async Task<ActionResult<Response<InitiativeDto>>> Reopen(Guid initiativeId, [FromBody] ReopenInitiativeCommandRequest request)
    {
        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    /// <summary>
    /// FRD lifecycle: puts an initiative on hold with a recorded reason. Gated on the view policy so the
    /// assigned owner can reach it; InitiativeGuard.EnsureCanManageLifecycle applies the real rule
    /// (edit/close permission or ownership), and the lifecycle guard centralizes the transition rules.
    /// </summary>
    [HttpPost("{initiativeId:guid}/hold")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> Hold(Guid initiativeId, [FromBody] PutInitiativeOnHoldCommandRequest request)
    {
        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    /// <summary>FRD lifecycle: resumes work on an initiative that was on hold.</summary>
    [HttpPost("{initiativeId:guid}/resume")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> Resume(Guid initiativeId, [FromBody] ResumeInitiativeCommandRequest request)
    {
        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    /// <summary>
    /// FRD lifecycle: cancels an initiative with a recorded reason. Gated on the view policy;
    /// InitiativeGuard.EnsureCanCancel reserves cancellation for edit/close holders (the
    /// owner-cancel workflow is an open decision tracked separately).
    /// </summary>
    [HttpPost("{initiativeId:guid}/cancel")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> Cancel(Guid initiativeId, [FromBody] CancelInitiativeCommandRequest request)
    {
        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    /// <summary>Adds a prerequisite link. Self-references, duplicates and cycles are rejected.</summary>
    [HttpPost("{initiativeId:guid}/dependencies")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> AddDependency(Guid initiativeId, [FromBody] AddInitiativeDependencyCommandRequest request)
    {
        if (request.InitiativeId == Guid.Empty)
            request.InitiativeId = initiativeId;
        else if (request.InitiativeId != initiativeId)
            return BadRequest("Initiative ID mismatch.");

        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpDelete("{initiativeId:guid}/dependencies/{dependsOnInitiativeId:guid}")]
    [Authorize(Policy = Permissions.InitiativesView)]
    public async Task<ActionResult<Response<InitiativeDto>>> RemoveDependency(Guid initiativeId, Guid dependsOnInitiativeId)
    {
        var result = await mediator.Send(new RemoveInitiativeDependencyCommandRequest
        {
            InitiativeId = initiativeId,
            DependsOnInitiativeId = dependsOnInitiativeId
        });

        return Ok(result);
    }
}