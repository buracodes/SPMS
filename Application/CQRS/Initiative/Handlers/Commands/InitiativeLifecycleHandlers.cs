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
/// FRD lifecycle: puts an initiative on hold. Authorized and transition-checked by
/// <see cref="InitiativeGuard"/>, which also centralizes the on-hold reason requirement, and
/// records an auditable history entry with reason, user and old/new status.
/// </summary>
public class PutInitiativeOnHoldCommandHandler : IRequestHandler<PutInitiativeOnHoldCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly ICurrentUserService currentUserService;
    private readonly InitiativeGuard guard;

    public PutInitiativeOnHoldCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory,
        ICurrentUserService _currentUserService,
        InitiativeGuard _guard)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
        currentUserService = _currentUserService;
        guard = _guard;
    }

    public async Task<Response<InitiativeDto>> Handle(PutInitiativeOnHoldCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        guard.EnsureMutable(initiative);
        guard.EnsureCanManageLifecycle(initiative);
        InitiativeGuard.EnsureCanTransition(initiative, Domain.Enums.InitiativeStatus.OnHold);
        var reason = InitiativeGuard.RequireLifecycleReason(request.Reason);

        var previousStatus = initiative.Status;
        initiative.Status = Domain.Enums.InitiativeStatus.OnHold;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeHeld",
            Details = $"Initiative put on hold. Reason: {reason}.",
            PreviousValue = previousStatus.ToString(),
            NewValue = Domain.Enums.InitiativeStatus.OnHold.ToString()
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative put on hold.",
            Data = await dtoFactory.BuildDetailAsync(detail!, cancellationToken)
        };
    }
}

/// <summary>
/// FRD lifecycle: resumes work on an initiative that was on hold, under the same guard rules as
/// holding. The resume reason is optional (it is the flip side of the reasoned hold decision).
/// </summary>
public class ResumeInitiativeCommandHandler : IRequestHandler<ResumeInitiativeCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly InitiativeGuard guard;

    public ResumeInitiativeCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory,
        InitiativeGuard _guard)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
        guard = _guard;
    }

    public async Task<Response<InitiativeDto>> Handle(ResumeInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        guard.EnsureMutable(initiative);
        guard.EnsureCanManageLifecycle(initiative);
        InitiativeGuard.EnsureCanTransition(initiative, Domain.Enums.InitiativeStatus.InProgress);

        var previousStatus = initiative.Status;
        initiative.Status = Domain.Enums.InitiativeStatus.InProgress;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var details = string.IsNullOrWhiteSpace(request.Reason)
            ? "Initiative resumed."
            : $"Initiative resumed. Reason: {request.Reason.Trim()}";

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeResumed",
            Details = details,
            PreviousValue = previousStatus.ToString(),
            NewValue = Domain.Enums.InitiativeStatus.InProgress.ToString()
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative resumed.",
            Data = await dtoFactory.BuildDetailAsync(detail!, cancellationToken)
        };
    }
}

/// <summary>
/// FRD lifecycle: cancels an planned or underway initiative. Reserved for edit/close users (the
/// owner-cancel workflow is an open decision), rejected when the transition is invalid, and
/// recorded with reason, user and old/new status.
/// </summary>
public class CancelInitiativeCommandHandler : IRequestHandler<CancelInitiativeCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly InitiativeGuard guard;

    public CancelInitiativeCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory,
        InitiativeGuard _guard)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
        guard = _guard;
    }

    public async Task<Response<InitiativeDto>> Handle(CancelInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        guard.EnsureMutable(initiative);
        guard.EnsureCanCancel(initiative);
        InitiativeGuard.EnsureCanTransition(initiative, Domain.Enums.InitiativeStatus.Cancelled);
        var reason = InitiativeGuard.RequireLifecycleReason(request.Reason);

        var previousStatus = initiative.Status;
        initiative.Status = Domain.Enums.InitiativeStatus.Cancelled;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeCancelled",
            Details = $"Initiative cancelled. Reason: {reason}.",
            PreviousValue = previousStatus.ToString(),
            NewValue = Domain.Enums.InitiativeStatus.Cancelled.ToString()
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative cancelled.",
            Data = await dtoFactory.BuildDetailAsync(detail!, cancellationToken)
        };
    }
}