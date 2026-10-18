using Application.CQRS.RagThreshold.Requests.Queries;
using Application.DTOS.RagThreshold;
using Application.Interface.IService;
using Application.Responses;
using MediatR;

namespace Application.CQRS.RagThreshold.Handlers.Queries
{
    public class GetRagThresholdsQueryHandler : IRequestHandler<GetRagThresholdsQueryRequest, Response<RagThresholdsDto>>
    {
        private readonly IRagThresholdService ragThresholdService;

        public GetRagThresholdsQueryHandler(IRagThresholdService _ragThresholdService)
        {
            ragThresholdService = _ragThresholdService;
        }

        public async Task<Response<RagThresholdsDto>> Handle(GetRagThresholdsQueryRequest request, CancellationToken cancellationToken)
        {
            var configuration = await ragThresholdService.GetConfigurationAsync(cancellationToken: cancellationToken);

            return new Response<RagThresholdsDto>
            {
                Success = true,
                Message = configuration.Source == "Defaults"
                    ? "Using default RAG thresholds (FR-607): Green 95%, Yellow 70%."
                    : "RAG thresholds loaded from configuration.",
                Data = configuration.ToDto()
            };
        }
    }
}
