using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands;

public class EnterDivisionActualValueCommandRequest : IRequest<Response<DivisionActualValueDto>>
{
    public required EnterDivisionActualValueDto Dto { get; set; }
}
