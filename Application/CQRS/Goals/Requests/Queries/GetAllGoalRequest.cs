using Application.DTOS.Goal;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Goals.Requests.Queries
{
    public class GetAllGoalRequest : IRequest<Response<List<GetAllGoalDto>>>
    {
        public Guid? ScorecardId { get; set; }

        /// <summary>Resolved through the goal's scorecard, so no fiscal year column is needed on Goal.</summary>
        public Guid? FiscalYearId { get; set; }
    }
}