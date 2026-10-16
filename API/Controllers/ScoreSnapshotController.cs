using Application.CQRS.ScoreSnapshot.Requests.Queries;
using Application.Interface.IService;
using Application.Responses;
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
    public class ScoreSnapshotController : ControllerBase
    {
        private readonly IMediator mediator;
        private readonly IScoreCalculationService scoreCalculationService;

        public ScoreSnapshotController(IMediator _mediator, IScoreCalculationService _scoreCalculationService)
        {
            mediator = _mediator;
            scoreCalculationService = _scoreCalculationService;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.DashboardView)]
        public async Task<ActionResult> Get(
            [FromQuery] Guid scorecardId, 
            [FromQuery] Guid fiscalYearId,
            [FromQuery] PeriodType? periodType,
            [FromQuery] int? quarter,
            [FromQuery] int? fromMonth,
            [FromQuery] int? toMonth,
            [FromQuery] int? year)
        {
            var result = await mediator.Send(new GetScoreSnapshotsQueryRequest 
            { 
                ScorecardId = scorecardId, 
                FiscalYearId = fiscalYearId,
                PeriodType = periodType,
                Quarter = quarter,
                FromMonth = fromMonth,
                ToMonth = toMonth,
                Year = year
            });
            return Ok(result);
        }

        [HttpPost("recalculate")]
        [Authorize(Policy = Permissions.AdminConfigManage)]
        public async Task<IActionResult> Recalculate([FromQuery] Guid scorecardId, [FromQuery] Guid fiscalPeriodId)
        {
            await scoreCalculationService.RecalculateScorecardAsync(scorecardId, fiscalPeriodId);
            return Ok(new Response<object>
            {
                Success = true,
                Message = "Scorecard recalculation triggered.",
                Data = new { ScorecardId = scorecardId, FiscalPeriodId = fiscalPeriodId }
            });
        }
    }
}
