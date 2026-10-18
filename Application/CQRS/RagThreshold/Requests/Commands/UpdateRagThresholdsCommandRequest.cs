using Application.DTOS.RagThreshold;
using Application.Responses;
using MediatR;

namespace Application.CQRS.RagThreshold.Requests.Commands
{
    public class UpdateRagThresholdsCommandRequest : IRequest<Response<RagThresholdsDto>>
    {
        public required UpdateRagThresholdsDto Dto { get; set; }
    }
}
