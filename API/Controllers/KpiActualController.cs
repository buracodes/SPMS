using Application.CQRS.KpiActuals.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
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
    public class KpiActualController : ControllerBase
    {
        private readonly IMediator mediator;
        public KpiActualController(IMediator _mediator)
        {
            mediator = _mediator;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.ActualsView)]
        public async Task<ActionResult> Get([FromQuery] Guid kpiId, [FromQuery] Guid fiscalYearId)
        {
            var result = await mediator.Send(new GetActualValuesQueryRequest { KpiId = kpiId, FiscalYearId = fiscalYearId });
            return Ok(result);
        }

        [HttpGet("history")]
        [Authorize(Policy = Permissions.ActualsView)]
        public async Task<ActionResult> History([FromQuery] Guid kpiId, [FromQuery] Guid fiscalPeriodId)
        {
            var result = await mediator.Send(new GetActualValueHistoryQueryRequest { KpiId = kpiId, FiscalPeriodId = fiscalPeriodId });
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.ActualsEnter)]
        public async Task<ActionResult> Enter([FromBody] EnterActualValueDto dto)
        {
            var result = await mediator.Send(new EnterActualValueCommandRequest { Dto = dto });
            return StatusCode(StatusCodes.Status201Created, result);
        }
    }
}
