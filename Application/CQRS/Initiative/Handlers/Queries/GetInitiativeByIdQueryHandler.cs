using Application.CQRS.Initiative.Requests.Queries;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Queries;

/// <summary>Initiative detail with backend-calculated progress, budget and closure state (FR-401 to FR-405).</summary>
public class GetInitiativeByIdQueryHandler : IRequestHandler<GetInitiativeByIdQueryRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly InitiativeDtoFactory dtoFactory;

    public GetInitiativeByIdQueryHandler(
        IInitiativeRepository _initiativeRepository,
        InitiativeDtoFactory _dtoFactory)
    {
        initiativeRepository = _initiativeRepository;
        dtoFactory = _dtoFactory;
    }

    public async Task<Response<InitiativeDto>> Handle(GetInitiativeByIdQueryRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetDetailAsync(request.Id)
            ?? throw new NotFoundException("Initiative", request.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative retrieved successfully.",
            Data = await dtoFactory.BuildDetailAsync(initiative, cancellationToken)
        };
    }
}
