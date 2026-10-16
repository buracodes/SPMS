using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>
/// FR-404 actual expenditure only. The narrow counterpart to
/// <see cref="UpdateInitiativeCommandHandler"/>: it writes nothing but the spend figure, so an
/// assigned owner can post expenditure on their own initiative without being able to change the
/// initiative details that <c>initiatives:edit</c> protects. Authorization and the FR-405 closed
/// lock are both delegated to <see cref="InitiativeGuard"/>, which is authoritative.
/// </summary>
public class UpdateInitiativeExpenditureCommandHandler
    : IRequestHandler<UpdateInitiativeExpenditureCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IMilestoneRepository milestoneRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeGuard guard;
    private readonly InitiativeDtoFactory dtoFactory;

    public UpdateInitiativeExpenditureCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IMilestoneRepository _milestoneRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeGuard _guard,
        InitiativeDtoFactory _dtoFactory)
    {
        initiativeRepository = _initiativeRepository;
        milestoneRepository = _milestoneRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        guard = _guard;
        dtoFactory = _dtoFactory;
    }

    public async Task<Response<InitiativeDto>> Handle(
        UpdateInitiativeExpenditureCommandRequest request,
        CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetByIdWithMilestonesAsync(request.Id)
            ?? throw new NotFoundException("Initiative", request.Id);

        // FR-405: a closed initiative stays read-only for its owner; only the correction
        // permissions may still write to it.
        guard.EnsureMutable(initiative);

        // Ownership / permission check before anything is written, so a refused post leaves the
        // recorded spend untouched.
        guard.EnsureCanUpdateExpenditure(initiative);

        InitiativeGuard.ValidateBudget(initiative.Budget, request.ActualExpenditure);

        var previous = initiative.ActualExpenditure;

        if (previous == request.ActualExpenditure)
        {
            var unchanged = await milestoneRepository.GetByInitiativeIdAsync(initiative.Id);
            return new Response<InitiativeDto>
            {
                Success = true,
                Message = "Actual expenditure is unchanged.",
                Data = dtoFactory.BuildSummary(initiative, unchanged)
            };
        }

        initiative.ActualExpenditure = request.ActualExpenditure;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Same audit mechanism as the detail update: one BudgetUpdated row carrying both values.
        await updateLogRepository.AddRange(new[]
        {
            new InitiativeUpdateLog
            {
                Id = Guid.NewGuid(),
                InitiativeId = initiative.Id,
                MilestoneId = null,
                ChangeType = "BudgetUpdated",
                Details = $"Actual expenditure updated from {previous:N2} to {initiative.ActualExpenditure:N2}.",
                PreviousValue = previous.ToString("N2"),
                NewValue = initiative.ActualExpenditure.ToString("N2")
            }
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var milestones = await milestoneRepository.GetByInitiativeIdAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Actual expenditure updated successfully.",
            Data = dtoFactory.BuildSummary(initiative, milestones)
        };
    }
}
