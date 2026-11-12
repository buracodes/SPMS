using Application.DTOS.KpiActual;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiActuals.Requests.Queries
{
    /// <summary>
    /// Loads the Data Entry board for one fiscal year, narrowed to the KPIs the signed-in
    /// principal reports on. There is no scorecard filter: the board returns every scorecard
    /// holding entry-scoped KPIs so the page can offer the picker in the same request.
    /// </summary>
    public class GetEntryBoardQueryRequest : IRequest<Response<EntryBoardDto>>
    {
        public Guid FiscalYearId { get; set; }
    }
}
