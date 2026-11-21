using Application.DTOS.KpiActual;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Queries;

public class GetDivisionActualValueHistoryQueryRequest : IRequest<Response<ActualValueHistoryDto>>
{
    public Guid DivisionId { get; set; }

    public Guid ScorecardId { get; set; }

    public Guid KpiId { get; set; }

    public Guid FiscalPeriodId { get; set; }
}
