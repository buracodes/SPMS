using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Calculation;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>
/// FR-405 initiative closure — the only way an initiative becomes Completed. Records the closure
/// information, locks the initiative so it becomes read-only for normal users, and is authorised as
/// an explicit business decision rather than a side effect of progress.
/// </summary>
public class CloseInitiativeCommandHandler : IRequestHandler<CloseInitiativeCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly IInitiativeProgressCalculator progressCalculator;
    private readonly ICurrentUserService currentUserService;
    private readonly InitiativeGuard guard;

    public CloseInitiativeCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory,
        IInitiativeProgressCalculator _progressCalculator,
        ICurrentUserService _currentUserService,
        InitiativeGuard _guard)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
        progressCalculator = _progressCalculator;
        currentUserService = _currentUserService;
        guard = _guard;
    }

    public async Task<Response<InitiativeDto>> Handle(CloseInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        var dto = request.Dto ?? throw new BadRequestException("Closure information is required.");

        var initiative = await initiativeRepository.GetByIdWithMilestonesAsync(dto.InitiativeId)
            ?? throw new NotFoundException("Initiative", dto.InitiativeId);

        if (initiative.IsClosed)
        {
            throw new BadRequestException("This initiative is already closed.");
        }

        // FRD lifecycle: completion is confirmed by a Power User or the assigned owner, not by
        // whoever happens to reach the endpoint.
        guard.EnsureCanClose(initiative);

        // FR-404: final spend must be a valid, non-negative budget figure. It defaults to the
        // expenditure recorded to date, and is the figure locked in by the closure.
        var finalExpenditure = dto.FinalActualExpenditure ?? initiative.ActualExpenditure;
        InitiativeGuard.ValidateBudget(initiative.Budget, finalExpenditure);

        if (dto.ActualEndDate == default)
        {
            throw new BadRequestException("Actual End Date is required.");
        }

        if (dto.ActualEndDate.Date > DateTime.Today)
        {
            throw new BadRequestException("Actual End Date must be less than or equal to today's date.");
        }

        // FR-405: the actual end date must fall inside the initiative window — after the start date
        // and no later than the planned end date.
        InitiativeGuard.ValidateActualEndDate(initiative, dto.ActualEndDate);

        // The FRD workflow closes an initiative once delivery is finished, so the recalculated
        // progress must actually be complete. Reported explicitly rather than silently accepted.
        var progress = progressCalculator.Calculate(InitiativeDtoFactory.ToProgressInputs(initiative.Milestones));
        var blocked = InitiativeGuard.DescribeWhyNotReadyForCompletion(progress);
        if (blocked is not null)
        {
            throw new BadRequestException(blocked);
        }

        var previousStatus = initiative.Status;
        var previousExpenditure = initiative.ActualExpenditure;

        initiative.ActualEndDate = dto.ActualEndDate;
        // Closure locks the final figure. It also becomes the expenditure-to-date reading, so the
        // closed initiative reports what was actually spent rather than an interim number.
        initiative.ActualExpenditure = finalExpenditure;
        // FR-405: the outcome summary and lessons learned are both optional; an empty summary is
        // stored as null rather than recorded as untouched whitespace.
        initiative.OutcomeSummary = string.IsNullOrWhiteSpace(dto.OutcomeSummary)
            ? null
            : dto.OutcomeSummary.Trim();
        initiative.LessonsLearned = string.IsNullOrWhiteSpace(dto.LessonsLearned)
            ? null
            : dto.LessonsLearned.Trim();
        initiative.Status = Domain.Enums.InitiativeStatus.Completed;
        initiative.IsClosed = true;
        initiative.ClosedAtUtc = DateTime.UtcNow;
        initiative.ClosedByUserId = currentUserService.UserId;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeClosed",
            Details = $"Initiative closed on {dto.ActualEndDate:yyyy-MM-dd} with final expenditure {finalExpenditure:N2}.",
            PreviousValue = previousStatus.ToString(),
            NewValue = $"{Domain.Enums.InitiativeStatus.Completed} / Closed"
        });

        // FR-404: a final figure that differs from the recorded expenditure is a budget correction,
        // so it gets its own auditable entry rather than being buried in the closure record.
        if (finalExpenditure != previousExpenditure)
        {
            await updateLogRepository.Add(new InitiativeUpdateLog
            {
                Id = Guid.NewGuid(),
                InitiativeId = initiative.Id,
                ChangeType = "BudgetUpdated",
                Details = $"Final actual expenditure set to {finalExpenditure:N2} at closure.",
                PreviousValue = previousExpenditure.ToString("N2"),
                NewValue = finalExpenditure.ToString("N2")
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative completed and closed successfully. It is now read-only for normal users.",
            Data = dtoFactory.BuildDetail(detail!)
        };
    }
}

/// <summary>
/// Reopens a closed initiative so corrections can continue. Reserved for users holding the
/// close permission, which the controller enforces.
/// </summary>
public class ReopenInitiativeCommandHandler : IRequestHandler<ReopenInitiativeCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;

    public ReopenInitiativeCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
    }

    public async Task<Response<InitiativeDto>> Handle(ReopenInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        if (!initiative.IsClosed)
        {
            throw new BadRequestException("This initiative is not closed.");
        }

        // Reopening is a correction to a recorded closure, so the reason is required and travels with
        // the history entry; it is never dropped. Existing audit rows are preserved untouched.
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BadRequestException("A reason is required to reopen an initiative.");
        }

        var reason = request.Reason.Trim();
        var previousStatus = initiative.Status;

        initiative.IsClosed = false;
        initiative.ClosedAtUtc = null;
        initiative.ClosedByUserId = null;
        initiative.Status = Domain.Enums.InitiativeStatus.InProgress;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeReopened",
            Details = $"Initiative reopened. Reason: {reason}",
            PreviousValue = previousStatus.ToString(),
            NewValue = Domain.Enums.InitiativeStatus.InProgress.ToString()
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Initiative reopened.",
            Data = dtoFactory.BuildDetail(detail!)
        };
    }
}
