using Application.DTOS.PeriodLock;
using Application.Responses;
using MediatR;
using System;

namespace Application.CQRS.PeriodLock.Requests.Queries
{
    public class GetPeriodLockStatusQueryRequest : IRequest<Response<GetPeriodLockStatusDto>>
    {
        public Guid ScorecardId { get; set; }
        public Guid FiscalYearId { get; set; }
    }
}
