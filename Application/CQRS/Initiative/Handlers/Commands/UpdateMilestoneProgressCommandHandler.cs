using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>
    /// FR-403 progress update. The owner or an authorised power user reports progress on a single
    /// milestone; the milestone status follows completion automatically; the recalculated
    /// initiative progress is returned by the backend and every change is written to the
    /// initiative history with the acting user and timestamp.
    /// <para>
    /// FRD lifecycle: the first reported progress also starts the initiative — a <c>NotStarted</c>
    /// initiative moves to <c>InProgress</c> the moment work is recorded. Reaching 100% makes the
    /// initiative <em>ready</em> for completion, but only formal closure sets Completed.
    /// </para>
/// </summary>
public class UpdateMilestoneProgressCommandHandler
    : IRequestHandler<UpdateMilestoneProgressCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IMilestoneRepository milestoneRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeGuard guard;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly ICurrentUserService currentUserService;

    public UpdateMilestoneProgressCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IMilestoneRepository _milestoneRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeGuard _guard,
        InitiativeDtoFactory _dtoFactory,
        ICurrentUserService _currentUserService)
    {
        initiativeRepository = _initiativeRepository;
        milestoneRepository = _milestoneRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        guard = _guard;
        dtoFactory = _dtoFactory;
        currentUserService = _currentUserService;
    }

    public async Task<Response<InitiativeDto>> Handle(
        UpdateMilestoneProgressCommandRequest request,
        CancellationToken cancellationToken)
    {
        var dto = request.Dto ?? throw new BadRequestException("Progress update data is required.");

        var initiative = await initiativeRepository.GetById(dto.InitiativeId)
            ?? throw new NotFoundException("Initiative", dto.InitiativeId);

        // FR-405: a closed initiative is read-only.
        guard.EnsureMutable(initiative);

        // FR-403: owner or authorised power user only.
        guard.EnsureCanUpdateProgress(initiative);

        var milestone = await milestoneRepository.GetByIdForInitiativeAsync(dto.MilestoneId, initiative.Id)
            ?? throw new NotFoundException("Milestone", dto.MilestoneId);

        if (!currentUserService.IsAuthenticated)
        {
            throw new ForbiddenException("An authenticated user is required to update initiative progress.");
        }

        InitiativeGuard.ValidateMilestoneRanges(milestone.Weight, dto.CompletionPercentage);

        var previousCompletion = milestone.CompletionPercentage;
        var previousStatus = milestone.Status;
        var previousInitiativeStatus = initiative.Status;

        milestone.CompletionPercentage = dto.CompletionPercentage;
        milestone.Status = InitiativeGuard.ResolveMilestoneStatus(
            dto.CompletionPercentage,
            dto.Status ?? previousStatus);

        // FRD lifecycle: the first reported progress starts the initiative (NotStarted -> InProgress).
        // That is the only transition a progress edit may perform — OnHold/Cancelled decisions,
        // Completed and an already-started initiative are never undone, and 0% starts nothing.
        InitiativeGuard.StartExecution(initiative, milestone.CompletionPercentage);

        if (dto.Comments is not null)
        {
            milestone.Comments = dto.Comments;
        }

        await milestoneRepository.Update(milestone);

        // Re-read with milestones so progress is recalculated by the authoritative calculator and
        // the client receives the figure instead of computing it itself.
        var updated = await initiativeRepository.GetDetailAsync(initiative.Id)
            ?? throw new NotFoundException("Initiative", initiative.Id);

        // FRD lifecycle: progress and status are independent except for one transition — starting a
        // NotStarted initiative (done above). Reaching 100% makes the initiative ready for completion
        // without completing it, and a deliberate OnHold/Cancelled state is likewise untouched.

        // FR-403: append history (never overwrite) with acting user, timestamp and old/new values.
        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            MilestoneId = milestone.Id,
            ChangeType = "ProgressUpdated",
            Details = BuildDetails(milestone, dto.Comments),
            PreviousValue = FormatValue(previousCompletion, previousStatus),
            NewValue = FormatValue(milestone.CompletionPercentage, milestone.Status)
        });

        if (previousInitiativeStatus != initiative.Status)
        {
            // FRD lifecycle: starting the initiative is a status change and is audited like any other.
            await updateLogRepository.Add(new InitiativeUpdateLog
            {
                Id = Guid.NewGuid(),
                InitiativeId = initiative.Id,
                ChangeType = "StatusChanged",
                Details = $"Status changed from {previousInitiativeStatus} to {initiative.Status}.",
                PreviousValue = previousInitiativeStatus.ToString(),
                NewValue = initiative.Status.ToString()
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = BuildMessage(dtoFactory.BuildProgress(updated)),
            Data = await dtoFactory.BuildDetailAsync(updated, cancellationToken)
        };
    }

    /**
     * Reports progress, and calls out when that progress has made the initiative ready for the
     * explicit completion step instead of silently completing it.
     */
    private static string BuildMessage(Domain.Calculation.InitiativeProgressResult progress)
    {
        var recalculated = $"Milestone progress updated. Initiative progress recalculated.";

        return InitiativeGuard.IsReadyForCompletion(progress)
            ? $"{recalculated} Delivery is complete — an authorised user can now confirm completion."
            : recalculated;
    }

    private static string FormatValue(int completion, Domain.Enums.MilestoneStatus status) =>
        $"{completion}% / {status}";

    private static string BuildDetails(Milestone milestone, string? comments)
    {
        var parts = new List<string>
        {
            $"{milestone.Name}: {milestone.CompletionPercentage}% complete."
        };

        if (!string.IsNullOrWhiteSpace(comments))
        {
            parts.Add($"Comment: {comments.Trim()}");
        }

        return string.Join(" ", parts);
    }
}
