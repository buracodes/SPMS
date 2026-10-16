using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>FR-401 initiative update. Rejects changes to a closed initiative (FR-405).</summary>
public class UpdateInitiativeCommandHandler : IRequestHandler<UpdateInitiativeCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IMilestoneRepository milestoneRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeGuard guard;
    private readonly InitiativeDtoFactory dtoFactory;

    public UpdateInitiativeCommandHandler(
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

    public async Task<Response<InitiativeDto>> Handle(UpdateInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetByIdWithMilestonesAsync(request.Id)
            ?? throw new NotFoundException("Initiative", request.Id);

// FR-405: a closed initiative is read-only for everyone without the correction permission,
        // which the controller has already filtered before reaching this handler.
        guard.EnsureMutable(initiative);

        // FRD budget rule: posting spend against an initiative is a Power User or owner action.
        // Checked before anything is written so a refused change leaves no partial edit behind.
        if (request.ActualExpenditure.HasValue)
        {
            guard.EnsureCanUpdateExpenditure(initiative);
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != initiative.Name)
        {
            var newName = request.Name.Trim();
            if (await initiativeRepository.ExistsByNameAsync(newName, initiative.Id))
            {
                throw new BadRequestException($"An initiative named '{newName}' already exists.");
            }
            initiative.Name = newName;
        }

        if (request.Description is not null)
        {
            initiative.Description = request.Description;
        }

        if (request.OwnerId.HasValue)
        {
            initiative.OwnerId = request.OwnerId.Value;
        }

// Strategic links: Guid? cannot distinguish "field omitted" from "explicitly cleared".
        // Frontends that render all three selects send StrategicLinksSpecified with the exact
        // values (empty selects become null), giving an authoritative set that may also clear.
        // ClearStrategicLinks remains the coarse "remove everything" action, and the legacy fallback
        // keeps supplying a subset merging with the links that stay unchanged.
        if (request.ClearStrategicLinks)
        {
            initiative.GoalId = null;
            initiative.KraId = null;
            initiative.KpiId = null;
        }
        else if (request.StrategicLinksSpecified)
        {
            await guard.ValidateStrategicLinksAsync(request.GoalId, request.KraId, request.KpiId, cancellationToken);

            initiative.GoalId = request.GoalId;
            initiative.KraId = request.KraId;
            initiative.KpiId = request.KpiId;
        }
        else if (request.GoalId.HasValue || request.KraId.HasValue || request.KpiId.HasValue)
        {
            var goalId = request.GoalId ?? initiative.GoalId;
            var kraId = request.KraId ?? initiative.KraId;
            var kpiId = request.KpiId ?? initiative.KpiId;

            await guard.ValidateStrategicLinksAsync(goalId, kraId, kpiId, cancellationToken);

            initiative.GoalId = goalId;
            initiative.KraId = kraId;
            initiative.KpiId = kpiId;
        }

        if (request.Priority.HasValue)
        {
            initiative.Priority = request.Priority.Value;
        }

        // FRD lifecycle: status is the initiative's execution state and is only ever moved through
        // the dedicated lifecycle actions (hold, resume, cancel, close, reopen). A bare detail edit
        // can never flip it, so anything sent here is refused instead of silently honoured or ignored.
        if (request.Status.HasValue)
        {
            throw new BadRequestException(
                "Initiative status can only be changed through the dedicated lifecycle actions: put on hold, resume, cancel, close or reopen.");
        }

        var startDate = request.StartDate ?? initiative.StartDate;
        var endDate = request.EndDate ?? initiative.EndDate;
        InitiativeGuard.ValidateDateRange(startDate, endDate);

        // FR-401 no-backdating rule: only a start date the caller is actually moving has to be
        // today or later. An initiative whose recorded start date is already in the past stays
        // editable, so correcting an overdue initiative never forces its start date forward.
        if (request.StartDate.HasValue && request.StartDate.Value.Date != initiative.StartDate.Date)
        {
            InitiativeGuard.ValidateStartDate(request.StartDate.Value);
        }

        initiative.StartDate = startDate;
        initiative.EndDate = endDate;

        var planned = request.Budget ?? initiative.Budget;
        var actual = request.ActualExpenditure ?? initiative.ActualExpenditure;
        InitiativeGuard.ValidateBudget(planned, actual);

        if (request.Budget.HasValue)
        {
            initiative.Budget = planned;
        }

        if (request.ActualExpenditure.HasValue)
        {
            initiative.ActualExpenditure = actual;
        }

        if (request.Resources is not null)
        {
            initiative.Resources = request.Resources;
        }

        var milestones = await milestoneRepository.GetByInitiativeIdAsync(initiative.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

// FR-403/FR-404 audit trail: record budget changes with previous/new values.
        var logs = new List<InitiativeUpdateLog>();

        if (request.ActualExpenditure.HasValue)
        {
            logs.Add(NewLog(initiative.Id, null, "BudgetUpdated",
                $"Actual expenditure updated to {initiative.ActualExpenditure:N2}.",
                null, initiative.ActualExpenditure.ToString("N2")));
        }

        if (request.Budget.HasValue)
        {
            logs.Add(NewLog(initiative.Id, null, "BudgetUpdated",
                $"Planned budget updated to {initiative.Budget:N2}.",
                null, initiative.Budget.ToString("N2")));
        }

        if (logs.Count > 0)
        {
            await updateLogRepository.AddRange(logs);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative updated successfully.",
            Data = dtoFactory.BuildSummary(initiative, milestones)
        };
    }

    private static InitiativeUpdateLog NewLog(
        Guid initiativeId,
        Guid? milestoneId,
        string changeType,
        string details,
        string? previousValue,
        string? newValue) => new()
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiativeId,
            MilestoneId = milestoneId,
            ChangeType = changeType,
            Details = details,
            PreviousValue = previousValue,
            NewValue = newValue
        };
}
