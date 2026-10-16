using MediatR;

namespace Application.CQRS.Kpi.Requests.Commands;

public class DeleteKpiCommandRequest : IRequest
{
    public Guid Id { get; set; }
}
