using Application.DTOS.Auth;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Requests.Commands
{
    public class LoginCommandRequest : IRequest<Response<TokenResponseDto>>
    {
        public required LoginDto Dto { get; set; }
    }
}