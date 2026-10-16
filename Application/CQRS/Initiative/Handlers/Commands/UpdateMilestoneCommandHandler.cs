using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>
/// FR-402 milestone update. Structural edits (name, dates, weight) are restricted to the owner
/// or an authorised power user; closed initiatives reject all changes.
/// </summary>
public class UpdateMilestoneCommandHandler : IRequestHandler<UpdateMilestoneCommandRequest, Response<MilestoneDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IMilestoneRepository milestoneRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeGuard guard;
    private readonly InitiativeDtoFactory dtoFactory;

    public UpdateMilestoneCommandHandler(
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

    public async Task<Response<MilestoneDto>> Handle(UpdateMilestoneCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        // FR-405: closed initiatives are read-only.
        guard.EnsureMutable(initiative);
        guard.EnsureCanManageMilestones(initiative);

        var milestone = await milestoneRepository.GetByIdForInitiativeAsync(request.Id, initiative.Id)
            ?? throw new NotFoundException("Milestone", request.Id);

        // Captured before mutation so the history records the real before/after pair.
        var previousCompletion = milestone.CompletionPercentage;
        var previousStatus = milestone.Status;

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != milestone.Name)
        {
            var newName = request.Name.Trim();
            if (await milestoneRepository.ExistsByNameAsync(initiative.Id, newName, milestone.Id))
            {
                throw new BadRequestException($"A milestone named '{newName}' already exists on this initiative.");
            }
            milestone.Name = newName;
        }

        if (request.Description is not null)
        {
            milestone.Description = request.Description;
        }

        if (request.DueDate.HasValue)
        {
            InitiativeGuard.ValidateMilestoneDueDate(initiative, request.DueDate.Value);
            milestone.DueDate = request.DueDate.Value;
        }

        var weight = request.Weight ?? milestone.Weight;
        var completion = request.CompletionPercentage ?? milestone.CompletionPercentage;
        InitiativeGuard.ValidateMilestoneRanges(weight, completion);

        if (request.Weight.HasValue)
        {
            // FR-402: the edited milestone is excluded from the running total so its own current
            // weight is not counted twice. Only checked when the weight actually moves, so an
            // initiative already over budget (legacy data) can still be corrected.
            if (request.Weight.Value != milestone.Weight)
            {
                var otherWeight = await milestoneRepository.GetTotalWeightAsync(initiative.Id, milestone.Id);
                InitiativeGuard.ValidateMilestoneWeightBudget(otherWeight, weight);
            }

            milestone.Weight = weight;
        }

        if (request.CompletionPercentage.HasValue)
        {
            milestone.CompletionPercentage = completion;
            // Keep status consistent with completion, mirroring FR-403's automatic rule.
            milestone.Status = InitiativeGuard.ResolveMilestoneStatus(completion, milestone.Status);
        }

        if (request.Status.HasValue)
        {
            milestone.Status = InitiativeGuard.ResolveMilestoneStatus(milestone.CompletionPercentage, request.Status.Value);
        }

        if (request.Comments is not null)
        {
            milestone.Comments = request.Comments;
        }

        await milestoneRepository.Update(milestone);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            MilestoneId = milestone.Id,
            ChangeType = "MilestoneUpdated",
            Details = $"Milestone '{milestone.Name}' updated (weight {milestone.Weight:N2}%, {milestone.CompletionPercentage}% complete).",
            PreviousValue = $"{previousCompletion}% / {previousStatus}",
            NewValue = $"{milestone.CompletionPercentage}% / {milestone.Status}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Re-read the detail so the response carries this milestone's recalculated contribution
        // to the initiative progress (FR-403).
        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        // FRD lifecycle: the initiative status is not written here. A milestone edit changes progress
        // and readiness, never the recorded execution state.

        var updated = detail is null
            ? null
            : dtoFactory.BuildDetail(detail).Milestones.FirstOrDefault(m => m.Id == milestone.Id);

        return new Response<MilestoneDto>
        {
            Success = true,
            Message = "Milestone updated successfully.",
            Data = updated ?? dtoFactory.BuildMilestone(milestone, null)
        };
    }
}
