using Application.Responses;
using Application.DTOS.ScoreSnapshot;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.ScoreSnapshot.Requests.Queries;

public class GetScoreSnapshotsQueryRequest : IRequest<Response<GetScoreSnapshotsDto>>
{
    public Guid ScorecardId { get; set; }
    public Guid FiscalYearId { get; set; }
    public PeriodType? PeriodType { get; set; }
    public int? Quarter { get; set; }
    public int? FromMonth { get; set; }
    public int? ToMonth { get; set; }
    public int? Year { get; set; }
}
