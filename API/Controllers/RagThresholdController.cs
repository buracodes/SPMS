using Application.CQRS.RagThreshold.Requests.Commands;
using Application.CQRS.RagThreshold.Requests.Queries;
using Application.DTOS.RagThreshold;
using Asp.Versioning;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    /// <summary>
    /// Effective RAG thresholds (FR-607). Reading is open to every authenticated user because every
    /// client bands scores with these values; updating is restricted to Admin and Power User.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class RagThresholdController : ControllerBase
    {
        private readonly IMediator mediator;

        public RagThresholdController(IMediator _mediator)
        {
            mediator = _mediator;
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult> Get()
        {
            var result = await mediator.Send(new GetRagThresholdsQueryRequest());
            return Ok(result);
        }

        [HttpPut]
        [Authorize(Policy = Permissions.AdminRagThresholdsManage)]
        public async Task<ActionResult> Update([FromBody] UpdateRagThresholdsDto dto)
        {
            var result = await mediator.Send(new UpdateRagThresholdsCommandRequest { Dto = dto });
            return Ok(result);
        }
    }
}
