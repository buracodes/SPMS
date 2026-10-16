using Application.DTOS.Initiative;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Queries;

public class GetInitiativeByIdQueryRequest : IRequest<Response<InitiativeDto>>
{
    public Guid Id { get; set; }
}
