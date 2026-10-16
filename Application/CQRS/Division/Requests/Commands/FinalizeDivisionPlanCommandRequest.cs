using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands;

public class FinalizeDivisionPlanCommandRequest : IRequest<Response<DivisionPlanSummaryDto>>
{
    public Guid DivisionId { get; set; }

    public Guid ScorecardId { get; set; }
}