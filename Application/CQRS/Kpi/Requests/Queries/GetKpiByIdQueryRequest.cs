using Application.DTOS.Kpi;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kpi.Requests.Queries;

public class GetKpiByIdQueryRequest : IRequest<Response<KpiDto>>
{
    public Guid Id { get; set; }
}
