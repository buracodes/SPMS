using MediatR;

namespace Application.CQRS.Kra.Requests.Commands;

public class DeleteKraCommandRequest : IRequest
{
    public Guid Id { get; set; }
}
