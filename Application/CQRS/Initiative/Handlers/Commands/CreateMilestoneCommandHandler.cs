using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;


public class CreateMilestoneCommandHandler : IRequestHandler<CreateMilestoneCommandRequest, Response<MilestoneDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IMilestoneRepository milestoneRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeGuard guard;
    private readonly InitiativeDtoFactory dtoFactory;

    public CreateMilestoneCommandHandler(
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

    public async Task<Response<MilestoneDto>> Handle(CreateMilestoneCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        // FR-405: closed initiatives are read-only.
        guard.EnsureMutable(initiative);
        guard.EnsureCanManageMilestones(initiative);

        InitiativeGuard.ValidateMilestoneRanges(request.Weight, request.CompletionPercentage);
        InitiativeGuard.ValidateMilestoneDueDate(initiative, request.DueDate);

        if (await milestoneRepository.ExistsByNameAsync(initiative.Id, request.Name))
        {
            throw new BadRequestException($"A milestone named '{request.Name}' already exists on this initiative.");
        }

        // FR-402: every milestone draws from one shared 100% weight budget, so the new weight must
        // fit inside what the existing milestones have left. Rejected here rather than left to the
        // closure check, which would otherwise fail later with a far less obvious message.
        var existingWeight = await milestoneRepository.GetTotalWeightAsync(initiative.Id);
        InitiativeGuard.ValidateMilestoneWeightBudget(existingWeight, request.Weight);

        var milestone = new Milestone
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            CompletionPercentage = request.CompletionPercentage,
            DueDate = request.DueDate,
            Weight = request.Weight,
            // FR-403: completion drives status, so keep the two consistent on create too.
            Status = InitiativeGuard.ResolveMilestoneStatus(request.CompletionPercentage, MilestoneStatus.NotStarted),
            InitiativeId = initiative.Id
        };

        await unitOfWork.AddRangeAsync(new[] { milestone }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        // FRD lifecycle: adding a milestone never writes the initiative status. Progress and status
        // are independent; only formal closure sets Completed.

        var progress = await BuildProgressDtoAsync(initiative.Id, milestone.Id, cancellationToken);

        // FR-403 audit: a new milestone changes the weighted-progress denominator.
        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            MilestoneId = milestone.Id,
            ChangeType = "MilestoneCreated",
            Details = $"Milestone '{milestone.Name}' added with weight {milestone.Weight:N2}% and due date {milestone.DueDate:yyyy-MM-dd}.",
            NewValue = $"{milestone.CompletionPercentage}% / {milestone.Status}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<MilestoneDto>
        {
            Success = true,
            Message = "Milestone created successfully.",
            Data = progress
        };
    }

    /// <summary>
    /// FR-403 milestone delivery state. The three FRD states map onto completion directly:
    /// 100% is <c>Completed</c> automatically, 0% is <c>NotStarted</c>, and anything in between is
/// <c>InProgress</c>. <see cref="InitiativeGuard.ResolveMilestoneStatus"/> owns the rule so create,
/// update and progress reporting all agree.
    /// </summary>
    private async Task<MilestoneDto> BuildProgressDtoAsync(Guid initiativeId, Guid milestoneId, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetDetailAsync(initiativeId);
        var milestone = initiative?.Milestones.FirstOrDefault(m => m.Id == milestoneId);
        return dtoFactory.BuildMilestone(milestone!, null);
    }
}
