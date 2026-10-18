using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

public class CreateInitiativeCommandHandler : IRequestHandler<CreateInitiativeCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeGuard guard;
    private readonly InitiativeDtoFactory dtoFactory;

    public CreateInitiativeCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IUnitOfWork _unitOfWork,
        InitiativeGuard _guard,
        InitiativeDtoFactory _dtoFactory)
    {
        initiativeRepository = _initiativeRepository;
        unitOfWork = _unitOfWork;
        guard = _guard;
        dtoFactory = _dtoFactory;
    }

    public async Task<Response<InitiativeDto>> Handle(CreateInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        if (await initiativeRepository.ExistsByNameAsync(request.Name))
        {
            throw new BadRequestException($"An initiative named '{request.Name}' already exists.");
        }

        if (request.StartDate is not DateTime startDate)
        {
            throw new BadRequestException("Start date is required when creating an initiative.");
        }

        if (request.EndDate is not DateTime endDate)
        {
            throw new BadRequestException("End date is required when creating an initiative.");
        }

        InitiativeGuard.ValidateDateRange(request.StartDate, request.EndDate);
        InitiativeGuard.ValidateStartDate(startDate);
        InitiativeGuard.ValidateBudget(request.Budget, 0m);
        await guard.ValidateStrategicLinksAsync(request.GoalId, request.KraId, request.KpiId, cancellationToken);

        // FRD lifecycle: a new initiative starts Not Started and cannot arrive already completed —
        // completion is recorded through formal closure.
        if (request.Status == Domain.Enums.InitiativeStatus.Completed)
        {
            throw new BadRequestException(
                "A new initiative cannot be created as completed. Create it as Not Started, In Progress, On Hold or Cancelled, and record completion through closure.");
        }

        var initiative = new Domain.Entities.Initiative
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            GoalId = request.GoalId,
            KraId = request.KraId,
            KpiId = request.KpiId,
            OwnerId = request.OwnerId,
            Priority = request.Priority,
            Status = request.Status,
            StartDate = startDate,
            EndDate = endDate,
            Budget = request.Budget,
            ActualExpenditure = 0m,
            Resources = request.Resources
        };

        await initiativeRepository.Add(initiative);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative created successfully.",
            Data = await dtoFactory.BuildSummaryAsync(initiative, cancellationToken: cancellationToken)
        };
    }
}
