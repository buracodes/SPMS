using Application.DTOS.Scorecard;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Scorecard.Requests.Queries;

public class GetAllScorecardQueryRequest : IRequest<Response<List<GetAllScorecardDto>>>
{
    public Guid? FiscalYearId { get; set; }
}
