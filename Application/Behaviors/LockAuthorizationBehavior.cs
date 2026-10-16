using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Services;
using Domain.Constants;
using MediatR;

namespace Application.Behaviors;

public class LockAuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IPeriodLockStatusService lockStatusService;
    private readonly ICurrentUserService currentUserService;
    private readonly IBusinessClock businessClock;

    public LockAuthorizationBehavior(
        IKpiRepository _kpiRepository,
        IPeriodLockStatusService _lockStatusService,
        ICurrentUserService _currentUserService,
        IBusinessClock _businessClock)
    {
        kpiRepository = _kpiRepository;
        lockStatusService = _lockStatusService;
        currentUserService = _currentUserService;
        businessClock = _businessClock;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is EnterActualValueCommandRequest command && command.Dto != null)
        {
            await AuthorizeActualValueWriteAsync(command.Dto.KpiId, command.Dto.FiscalPeriodId, command.Dto.OverrideReason);
        }

        if (request is EnterDivisionActualValueCommandRequest divisionCommand && divisionCommand.Dto != null)
        {
            await AuthorizeActualValueWriteAsync(divisionCommand.Dto.KpiId, divisionCommand.Dto.FiscalPeriodId, divisionCommand.Dto.OverrideReason);
        }

        return await next();
    }

    private async Task AuthorizeActualValueWriteAsync(Guid kpiId, Guid fiscalPeriodId, string? overrideReason)
    {
        var kpi = await kpiRepository.GetByIdWithScorecardAsync(kpiId);
        if (kpi == null || kpi.Scorecard == null)
        {
            return;
        }

        var currentUserId = currentUserService.UserId;
        var isKpiOwner = currentUserId.HasValue && kpi.OwnerId.HasValue && kpi.OwnerId.Value == currentUserId.Value;
        var canOverride = currentUserService.Permissions.Contains(Permissions.ActualsOverride);

        // Closed fiscal year / scorecard (FR-703): read-only unless an audited
        // Admin/Power User override with a reason is supplied. A fiscal year whose
        // end date has passed in ECX business time is closed even before the nightly
        // closure job flips its status.
        if (ScorecardClosureGuard.IsClosed(kpi.Scorecard, businessClock.BusinessDate))
        {
            if (!canOverride)
            {
                throw new ScorecardClosedException(
                    scorecardId: kpi.Scorecard.Id,
                    message: "The scorecard's fiscal year is closed and read-only. Actual values can only be corrected by a user with override permission.");
            }

            if (string.IsNullOrWhiteSpace(overrideReason))
            {
                throw new BadRequestException("An override reason is required when entering values for a closed scorecard.");
            }

            return;
        }

        var status = await lockStatusService.GetStatusAsync(
            kpi.ScorecardId,
            fiscalPeriodId,
            DateTime.UtcNow);

        // If period is open, only KPI owner (or override users) can enter
        if (status.State == PeriodLockState.Open)
        {
            if (isKpiOwner || canOverride)
            {
                return;
            }
            throw new PeriodLockedException(
                kpi.ScorecardId,
                fiscalPeriodId,
                "Only the KPI owner can enter actual values during the open entry window.");
        }

        // Period is locked or not yet open - requires override permission
        if (!canOverride)
        {
            throw new PeriodLockedException(
                kpi.ScorecardId,
                fiscalPeriodId,
                $"The fiscal period is {status.State.ToString().ToLower()} for this scorecard. Actual values can only be entered during the entry window or by a user with override permission.");
        }

        if (string.IsNullOrWhiteSpace(overrideReason))
        {
            throw new BadRequestException("An override reason is required when entering values outside the entry window.");
        }
    }
}
