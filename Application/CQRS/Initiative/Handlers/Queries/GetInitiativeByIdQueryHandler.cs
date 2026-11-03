using Application.CQRS.Initiative.Requests.Queries;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Queries;

/// <summary>Initiative detail with backend-calculated progress, budget and closure state (FR-401 to FR-405).</summary>
public class GetInitiativeByIdQueryHandler : IRequestHandler<GetInitiativeByIdQueryRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly IOrganizationalScopeService organizationalScope;

    public GetInitiativeByIdQueryHandler(
        IInitiativeRepository _initiativeRepository,
        InitiativeDtoFactory _dtoFactory,
        IOrganizationalScopeService _organizationalScope)
    {
        initiativeRepository = _initiativeRepository;
        dtoFactory = _dtoFactory;
        organizationalScope = _organizationalScope;
    }

    public async Task<Response<InitiativeDto>> Handle(GetInitiativeByIdQueryRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetDetailAsync(request.Id)
            ?? throw new NotFoundException("Initiative", request.Id);

        await organizationalScope.EnsureInitiativeAsync(
            initiative.OwnerId, initiative.DivisionOwnerId, cancellationToken);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative retrieved successfully.",
            Data = await dtoFactory.BuildDetailAsync(initiative, cancellationToken)
        };
    }
}
