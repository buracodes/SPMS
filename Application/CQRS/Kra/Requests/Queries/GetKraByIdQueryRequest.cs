using Application.DTOS.Kra;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kra.Requests.Queries;

public class GetKraByIdQueryRequest : IRequest<Response<KraDto>>
{
    public Guid Id { get; set; }
}
