using MediatR;
using Application.Responses;
using Application.DTOS.Scorecard;

namespace Application.CQRS.Scorecard.Requests.Commands
{
    public class CloneScorecardCommandRequest : IRequest<Response<CloneScorecardResultDto>>
    {
        public Guid SourceScorecardId { get; set; }

        public Guid TargetFiscalYearId { get; set; }

        public string? NewName { get; set; }
    }
}
