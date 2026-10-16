using Application.DTOS.Goal;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Goals.Requests.Commands
{
    public class CreateGoalCommandRequest : IRequest<Response<CreateGoalDto>>
    {
        public required CreateGoalDto Dto { get; set; }
    }
}
