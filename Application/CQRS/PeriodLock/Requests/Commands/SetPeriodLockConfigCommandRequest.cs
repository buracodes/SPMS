using Application.DTOS.PeriodLock;
using Application.Responses;
using MediatR;

namespace Application.CQRS.PeriodLock.Requests.Commands
{
    public class SetPeriodLockConfigCommandRequest : IRequest<Response<PeriodLockStatusItemDto>>
    {
        public required SetPeriodLockConfigDto Dto { get; set; }
    }
}
