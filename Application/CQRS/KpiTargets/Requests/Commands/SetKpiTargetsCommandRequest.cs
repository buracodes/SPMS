using Application.DTOS.KpiTarget;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiTargets.Requests.Commands
{
    public class SetKpiTargetsCommandRequest : IRequest<Response<GetKpiTargetsDto>>
    {
        public required SetKpiTargetsDto Dto { get; set; }
    }
}
