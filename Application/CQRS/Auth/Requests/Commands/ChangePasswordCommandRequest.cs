using Application.DTOS.Auth;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Requests.Commands
{
    public class ChangePasswordCommandRequest : IRequest<Response<bool>>
    {
        public required ChangePasswordDto Dto { get; set; }
    }
}
