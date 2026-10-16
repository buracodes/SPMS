using Application.DTOS.Kra;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kra.Requests.Queries;

public class GetAllKraQueryRequest : IRequest<Response<List<GetAllKraDto>>>
{
    public Guid? ScorecardId { get; set; }
    public Guid? GoalId { get; set; }

    /// <summary>Resolved through the KRA's scorecard, so no fiscal year column is needed on Kra.</summary>
    public Guid? FiscalYearId { get; set; }
}
