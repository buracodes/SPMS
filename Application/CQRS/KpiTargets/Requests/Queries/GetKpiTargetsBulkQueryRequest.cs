using Application.DTOS.KpiTarget;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiTargets.Requests.Queries
{
    /// <summary>
    /// Loads the targets of every KPI in a fiscal year, optionally narrowed to one
    /// scorecard, so the Targets page can fill a whole grid in one request.
    /// </summary>
    public class GetKpiTargetsBulkQueryRequest : IRequest<Response<GetKpiTargetsBulkDto>>
    {
        public Guid FiscalYearId { get; set; }
        public Guid? ScorecardId { get; set; }
    }
}
