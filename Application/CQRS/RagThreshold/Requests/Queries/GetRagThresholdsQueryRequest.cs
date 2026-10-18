using Application.DTOS.RagThreshold;
using Application.Responses;
using MediatR;

namespace Application.CQRS.RagThreshold.Requests.Queries
{
    public class GetRagThresholdsQueryRequest : IRequest<Response<RagThresholdsDto>>
    {
    }
}
