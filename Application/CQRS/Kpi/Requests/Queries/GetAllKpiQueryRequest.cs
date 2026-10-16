using Application.DTOS.Kpi;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kpi.Requests.Queries;

public class GetAllKpiQueryRequest : IRequest<Response<List<GetAllKpiDto>>>
{
    public Guid? ScorecardId { get; set; }
    public Guid? KraId { get; set; }

    /// <summary>Resolved through the KPI's scorecard, so no fiscal year column is needed on Kpi.</summary>
    public Guid? FiscalYearId { get; set; }

    /// <summary>A KPI belongs to a goal through its KRA, so this narrows the KPI's KRA set.</summary>
    public Guid? GoalId { get; set; }
}
