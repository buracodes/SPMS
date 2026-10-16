using Application.DTOS.KpiActual;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiActuals.Requests.Commands
{
    public class EnterActualValueCommandRequest : IRequest<Response<ActualValueDto>>
    {
        public required EnterActualValueDto Dto { get; set; }
    }
}
