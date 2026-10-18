using Application.CQRS.RagThreshold.Requests.Commands;
using Application.DTOS.RagThreshold;
using Application.Interface.IService;
using Application.Responses;
using MediatR;

namespace Application.CQRS.RagThreshold.Handlers.Commands
{
    public class UpdateRagThresholdsCommandHandler : IRequestHandler<UpdateRagThresholdsCommandRequest, Response<RagThresholdsDto>>
    {
        private readonly IRagThresholdService ragThresholdService;

        public UpdateRagThresholdsCommandHandler(IRagThresholdService _ragThresholdService)
        {
            ragThresholdService = _ragThresholdService;
        }

        public async Task<Response<RagThresholdsDto>> Handle(UpdateRagThresholdsCommandRequest request, CancellationToken cancellationToken)
        {
            var dto = request.Dto ?? throw new Exceptions.BadRequestException("RAG threshold configuration is required.");

            var configuration = await ragThresholdService.UpdateAsync(dto.Green, dto.Yellow, cancellationToken: cancellationToken);

            return new Response<RagThresholdsDto>
            {
                Success = true,
                Message = $"RAG thresholds saved. Green >= {configuration.Green}%, Yellow {configuration.Yellow}-{configuration.Green}%, Red < {configuration.Yellow}%.",
                Data = configuration.ToDto()
            };
        }
    }
}
