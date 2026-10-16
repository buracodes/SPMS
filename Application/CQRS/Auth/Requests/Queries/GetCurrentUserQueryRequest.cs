using Application.DTOS.Auth;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Requests.Queries
{
    public class GetCurrentUserQueryRequest : IRequest<Response<AuthUserDto>>
    {
    }
}