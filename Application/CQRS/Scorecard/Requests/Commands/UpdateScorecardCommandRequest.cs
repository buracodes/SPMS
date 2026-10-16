using Application.DTOS.Scorecard;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Scorecard.Requests.Commands;

public class UpdateScorecardCommandRequest : IRequest<Response<ScorecardDto>>
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public ScorecardScope? Scope { get; set; }
}
