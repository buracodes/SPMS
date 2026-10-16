using Application.DTOS.Auth;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Requests.Commands
{
    public class RefreshTokenCommandRequest : IRequest<Response<TokenResponseDto>>
    {
        public required RefreshTokenDto Dto { get; set; }
    }
}