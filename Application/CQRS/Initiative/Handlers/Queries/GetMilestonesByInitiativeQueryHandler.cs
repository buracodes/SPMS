using Application.CQRS.Initiative.Requests.Queries;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Queries;

/// <summary>
/// FR-402 milestone list for one initiative, each milestone carrying its backend-calculated
/// contribution to the overall initiative progress.
/// </summary>
public class GetMilestonesByInitiativeQueryHandler
    : IRequestHandler<GetMilestonesByInitiativeQueryRequest, Response<List<MilestoneDto>>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly InitiativeDtoFactory dtoFactory;

    public GetMilestonesByInitiativeQueryHandler(
        IInitiativeRepository _initiativeRepository,
        InitiativeDtoFactory _dtoFactory)
    {
        initiativeRepository = _initiativeRepository;
        dtoFactory = _dtoFactory;
    }

    public async Task<Response<List<MilestoneDto>>> Handle(GetMilestonesByInitiativeQueryRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetDetailAsync(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        return new Response<List<MilestoneDto>>
        {
            Success = true,
            Message = "Milestones retrieved successfully.",
            Data = (await dtoFactory.BuildDetailAsync(initiative, cancellationToken)).Milestones
        };
    }
}
