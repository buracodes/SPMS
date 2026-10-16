using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Division.Requests.Queries;
using Asp.Versioning;
using Domain.Constants;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class DivisionController : ControllerBase
    {
        private readonly IMediator mediator;

        public DivisionController(IMediator _mediator)
        {
            mediator = _mediator;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.DivisionView)]
        public async Task<ActionResult> GetDivisions()
        {
            var result = await mediator.Send(new GetDivisionsQueryRequest());
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.DivisionManage)]
        public async Task<ActionResult> Create([FromBody] CreateDivisionCommandRequest request)
        {
            var result = await mediator.Send(request);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpGet("{divisionId:guid}/plans")]
        [Authorize(Policy = Permissions.DivisionView)]
        public async Task<ActionResult> GetPlans(Guid divisionId)
        {
            var result = await mediator.Send(new GetDivisionPlansQueryRequest { DivisionId = divisionId });
            return Ok(result);
        }

        [HttpGet("{divisionId:guid}/plans/{scorecardId:guid}")]
        [Authorize(Policy = Permissions.DivisionView)]
        public async Task<ActionResult> GetPlan(Guid divisionId, Guid scorecardId)
        {
            var result = await mediator.Send(new GetDivisionPlanQueryRequest { DivisionId = divisionId, ScorecardId = scorecardId });
            return Ok(result);
        }

        [HttpGet("plans/by-scorecard/{scorecardId:guid}")]
        [Authorize(Policy = Permissions.DivisionView)]
        public async Task<ActionResult> GetPlanContextByScorecard(
            Guid scorecardId,
            [FromQuery] Guid? divisionId = null,
            [FromQuery] PeriodType? periodType = null,
            [FromQuery] int? quarter = null,
            [FromQuery] int? fromMonth = null,
            [FromQuery] int? toMonth = null,
            [FromQuery] int? year = null)
        {
            // FR-908: previously these period filters were dropped here, so the report's
            // YTD/Month/Quarter/Year selection had no server-side effect.
            var result = await mediator.Send(new GetDivisionPlanContextQueryRequest
            {
                ScorecardId = scorecardId,
                DivisionId = divisionId,
                PeriodType = periodType,
                Quarter = quarter,
                FromMonth = fromMonth,
                ToMonth = toMonth,
                Year = year
            });
            return Ok(result);
        }

        [HttpPost("contributions")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> SetContributions([FromBody] SetDivisionContributionsCommandRequest request)
        {
            var result = await mediator.Send(request);
            return Ok(result);
        }

        [HttpPost("goals")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> CreateGoal([FromBody] CreateDivisionGoalCommandRequest request)
        {
            var result = await mediator.Send(request);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpPut("goals/{divisionGoalId:guid}")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> UpdateGoal(Guid divisionGoalId, [FromBody] UpdateDivisionGoalCommandRequest request)
        {
            request.DivisionGoalId = divisionGoalId;
            var result = await mediator.Send(request);
            return Ok(result);
        }

        [HttpDelete("goals/{divisionGoalId:guid}")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> DeleteGoal(Guid divisionGoalId)
        {
            var result = await mediator.Send(new DeleteDivisionGoalCommandRequest { DivisionGoalId = divisionGoalId });
            return Ok(result);
        }

        [HttpPost("kpis")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> CreateKpi([FromBody] CreateDivisionKpiCommandRequest request)
        {
            var result = await mediator.Send(request);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpPut("kpis/{divisionKpiId:guid}")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> UpdateKpi(Guid divisionKpiId, [FromBody] UpdateDivisionKpiCommandRequest request)
        {
            request.DivisionKpiId = divisionKpiId;
            var result = await mediator.Send(request);
            return Ok(result);
        }

        [HttpDelete("kpis/{divisionKpiId:guid}")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> DeleteKpi(Guid divisionKpiId)
        {
            var result = await mediator.Send(new DeleteDivisionKpiCommandRequest { DivisionKpiId = divisionKpiId });
            return Ok(result);
        }

        [HttpPost("plans/{divisionId:guid}/{scorecardId:guid}/finalize")]
        [Authorize(Policy = Permissions.DivisionPlan)]
        public async Task<ActionResult> FinalizePlan(Guid divisionId, Guid scorecardId)
        {
            var result = await mediator.Send(new FinalizeDivisionPlanCommandRequest
            {
                DivisionId = divisionId,
                ScorecardId = scorecardId
            });
            return Ok(result);
        }

        [HttpPost("actuals")]
        [Authorize(Policy = Permissions.DivisionActualsEnter)]
        public async Task<ActionResult> EnterActual([FromBody] EnterDivisionActualValueCommandRequest request)
        {
            var result = await mediator.Send(request);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpGet("actuals")]
        [Authorize(Policy = Permissions.DivisionView)]
        public async Task<ActionResult> GetActuals([FromQuery] Guid divisionId, [FromQuery] Guid fiscalYearId)
        {
            var result = await mediator.Send(new GetDivisionActualValuesQueryRequest
            {
                DivisionId = divisionId,
                FiscalYearId = fiscalYearId
            });
            return Ok(result);
        }
    }
}
