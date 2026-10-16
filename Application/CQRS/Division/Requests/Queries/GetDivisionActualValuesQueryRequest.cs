using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Queries;

public class GetDivisionActualValuesQueryRequest : IRequest<Response<IReadOnlyList<DivisionActualValueDto>>>
{
    public Guid DivisionId { get; set; }

    public Guid FiscalYearId { get; set; }
}
