using Application.CQRS.PeriodLock.Requests.Commands;
using Application.CQRS.PeriodLock.Requests.Queries;
using Application.DTOS.PeriodLock;
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
    public class PeriodLockController : ControllerBase
    {
        private readonly IMediator mediator;

        public PeriodLockController(IMediator _mediator)
        {
            mediator = _mediator;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.ActualsView)]
        public async Task<ActionResult> Get([FromQuery] Guid scorecardId, [FromQuery] Guid fiscalYearId)
        {
            var result = await mediator.Send(new GetPeriodLockStatusQueryRequest { ScorecardId = scorecardId, FiscalYearId = fiscalYearId });
            return Ok(result);
        }

        [HttpPut]
        [Authorize(Policy = Permissions.AdminConfigManage)]
        public async Task<ActionResult> Set([FromBody] SetPeriodLockConfigDto dto)
        {
            var result = await mediator.Send(new SetPeriodLockConfigCommandRequest { Dto = dto });
            return Ok(result);
        }
    }
}
