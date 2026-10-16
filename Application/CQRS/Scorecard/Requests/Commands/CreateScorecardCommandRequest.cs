using Application.DTOS.Scorecard;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Scorecard.Requests.Commands;

public class CreateScorecardCommandRequest : IRequest<Response<ScorecardDto>>
{
    public Guid FiscalYearId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Domain.Enums.ScorecardScope Scope { get; set; } = Domain.Enums.ScorecardScope.Corporate;
}
