using Application.CQRS.Auth.Requests.Commands;
using Application.CQRS.Auth.Requests.Queries;
using Application.DTOS.Auth;
using Application.Responses;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class AuthController : ControllerBase
    {
        private readonly IMediator mediator;
        public AuthController(IMediator _mediator)
        {
            mediator = _mediator;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<Response<TokenResponseDto>>> Login([FromBody] LoginDto dto)
        {
            var result = await mediator.Send(new LoginCommandRequest { Dto = dto });
            return Ok(result);
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<ActionResult<Response<TokenResponseDto>>> Refresh([FromBody] RefreshTokenDto dto)
        {
            var result = await mediator.Send(new RefreshTokenCommandRequest { Dto = dto });
            return Ok(result);
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<ActionResult<Response<bool>>> Logout([FromBody] LogoutDto dto)
        {
            var result = await mediator.Send(new LogoutCommandRequest { Dto = dto });
            return Ok(result);
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<Response<AuthUserDto>>> Me()
        {
            var result = await mediator.Send(new GetCurrentUserQueryRequest());
            return Ok(result);
        }
    }
}