using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class CreateDivisionCommandRequest : IRequest<Response<DivisionDto>>
    {
        public required string Name { get; set; }
        public required string Code { get; set; }
    }
}
