using Application.CQRS.KpiTargets.Requests.Commands;
using Application.CQRS.KpiTargets.Requests.Queries;
using Application.DTOS.KpiTarget;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class KpiTargetController : ControllerBase
    {
        private readonly IMediator mediator;
        public KpiTargetController(IMediator _mediator)
        {
            mediator = _mediator;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.TargetsView)]
        public async Task<ActionResult> Get([FromQuery] Guid kpiId, [FromQuery] Guid fiscalYearId)
        {
            var result = await mediator.Send(new GetKpiTargetsQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId });
            return Ok(result);
        }

        /// <summary>
        /// Targets of every KPI in a fiscal year, optionally narrowed to one scorecard, so the
        /// Targets page can fill a whole filtered grid with a single request.
        /// </summary>
        [HttpGet("bulk")]
        [Authorize(Policy = Permissions.TargetsView)]
        public async Task<ActionResult> GetBulk([FromQuery] Guid fiscalYearId, [FromQuery] Guid? scorecardId)
        {
            var result = await mediator.Send(new GetKpiTargetsBulkQueryRequest
            {
                FiscalYearId = fiscalYearId,
                ScorecardId = scorecardId
            });
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.TargetsEdit)]
        public async Task<ActionResult> Set([FromBody] SetKpiTargetsDto dto)
        {
            var result = await mediator.Send(new SetKpiTargetsCommandRequest { Dto = dto });
            return StatusCode(StatusCodes.Status201Created, result);
        }

        /// <summary>
        /// Saves the targets of several KPIs at once. Each KPI is validated on its own, and the
        /// response reports per-KPI success so a single invalid row cannot hide the others.
        /// </summary>
        [HttpPost("bulk")]
        [Authorize(Policy = Permissions.TargetsEdit)]
        public async Task<ActionResult> SetBulk([FromBody] SetKpiTargetsBulkDto dto)
        {
            var result = await mediator.Send(new SetKpiTargetsBulkCommandRequest { Dto = dto });
            return StatusCode(StatusCodes.Status201Created, result);
        }
    }
}
