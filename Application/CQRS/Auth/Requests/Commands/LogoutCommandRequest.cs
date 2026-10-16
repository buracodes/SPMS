using Application.DTOS.Auth;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Requests.Commands
{
    public class LogoutCommandRequest : IRequest<Response<bool>>
    {
        public required LogoutDto Dto { get; set; }
    }
}