using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands
{
    public class DeleteInitiativeCommandRequest : IRequest<Response<object>>
    {
        public required Guid Id { get; set; }
    }
}
