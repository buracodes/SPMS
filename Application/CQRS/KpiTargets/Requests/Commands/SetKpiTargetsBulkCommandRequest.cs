using Application.DTOS.KpiTarget;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiTargets.Requests.Commands
{
    /// <summary>
    /// Saves the targets of several KPIs at once. Each KPI is validated and saved on its
    /// own so one invalid row is reported without discarding the others.
    /// </summary>
    public class SetKpiTargetsBulkCommandRequest : IRequest<Response<SetKpiTargetsBulkResultDto>>
    {
        public required SetKpiTargetsBulkDto Dto { get; set; }
    }
}
