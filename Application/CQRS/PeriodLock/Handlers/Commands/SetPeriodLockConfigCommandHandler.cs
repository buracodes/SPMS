using Application.CQRS.PeriodLock.Requests.Commands;
using Application.DTOS.PeriodLock;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.PeriodLock.Handlers.Commands
{
    public class SetPeriodLockConfigCommandHandler : IRequestHandler<SetPeriodLockConfigCommandRequest, Response<PeriodLockStatusItemDto>>
    {
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IPeriodLockConfigRepository periodLockConfigRepository;
        private readonly IPeriodLockStatusService lockStatusService;
        private readonly ICurrentUserService currentUserService;

        public SetPeriodLockConfigCommandHandler(
            IScorecardRepository _scorecardRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IPeriodLockConfigRepository _periodLockConfigRepository,
            IPeriodLockStatusService _lockStatusService,
            ICurrentUserService _currentUserService)
        {
            scorecardRepository = _scorecardRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            periodLockConfigRepository = _periodLockConfigRepository;
            lockStatusService = _lockStatusService;
            currentUserService = _currentUserService;
        }

        public async Task<Response<PeriodLockStatusItemDto>> Handle(SetPeriodLockConfigCommandRequest request, CancellationToken cancellationToken)
        {
            var dto = request.Dto;
            if (dto == null)
            {
                throw new BadRequestException("Period lock configuration is required.");
            }

            if (dto.Lock && (dto.Unlock || dto.ForceOpen))
            {
                throw new BadRequestException("A period cannot be locked and unlocked/force-opened in the same request.");
            }

            if (dto.Unlock && dto.ForceOpen)
            {
                throw new BadRequestException("Cannot unlock and force-open in the same request.");
            }

            var scorecard = await scorecardRepository.GetById(dto.ScorecardId);
            if (scorecard == null)
            {
                throw new NotFoundException(nameof(Scorecard), dto.ScorecardId);
            }

            var period = await fiscalPeriodRepository.GetById(dto.FiscalPeriodId);
            if (period == null)
            {
                throw new NotFoundException(nameof(FiscalPeriod), dto.FiscalPeriodId);
            }

            if (period.FiscalYearId != scorecard.FiscalYearId)
            {
                throw new BadRequestException("The fiscal period does not belong to the scorecard's fiscal year.");
            }

            var existing = await periodLockConfigRepository.GetByScorecardAndPeriodAsync(dto.ScorecardId, dto.FiscalPeriodId);
            var isNew = existing == null;

            PeriodLockConfig config;
            if (isNew)
            {
                config = new PeriodLockConfig
                {
                    Id = Guid.NewGuid(),
                    ScorecardId = dto.ScorecardId,
                    FiscalPeriodId = dto.FiscalPeriodId,
                    EntryOpenDay = 1,
                    CutoffDay = 7,
                    ReminderDaysBeforeCutoff = 2
                };
            }
            else
            {
                config = existing!;
            }

            if (dto.EntryOpenDay.HasValue)
            {
                config.EntryOpenDay = dto.EntryOpenDay.Value;
            }

            if (dto.CutoffDay.HasValue)
            {
                config.CutoffDay = dto.CutoffDay.Value;
            }

            if (dto.ReminderDaysBeforeCutoff.HasValue)
            {
                config.ReminderDaysBeforeCutoff = dto.ReminderDaysBeforeCutoff.Value;
            }

            var userId = currentUserService.UserId;
            var now = DateTime.UtcNow;

            if (dto.Lock)
            {
                config.LockedAtUtc = now;
                config.LockedByUserId = userId;
                config.UnlockedAtUtc = null;
                config.UnlockedByUserId = null;
            }
            else if (dto.Unlock)
            {
                config.UnlockedAtUtc = now;
                config.UnlockedByUserId = userId;
            }
            else if (dto.ForceOpen)
            {
                // Force open: set UnlockedAtUtc without requiring prior lock
                config.UnlockedAtUtc = now;
                config.UnlockedByUserId = userId;
            }
            else
            {
                // Just saving window config (no lock/unlock/forceOpen) → clear explicit lock state
                // so date-based logic takes effect
                config.LockedAtUtc = null;
                config.LockedByUserId = null;
                config.UnlockedAtUtc = null;
                config.UnlockedByUserId = null;
            }

            if (isNew)
            {
                await periodLockConfigRepository.Add(config);
            }
            else
            {
                await periodLockConfigRepository.Update(config);
            }

            var status = await lockStatusService.GetStatusAsync(scorecard.Id, period, now);

            return new Response<PeriodLockStatusItemDto>
            {
                Success = true,
                Message = "Period lock configuration saved successfully.",
                Data = new PeriodLockStatusItemDto
                {
                    FiscalPeriodId = period.Id,
                    FiscalMonth = period.FiscalMonth,
                    Quarter = period.Quarter,
                    IsQuarterEnd = period.IsQuarterEnd,
                    IsYearEnd = period.IsYearEnd,
                    State = status.State,
                    EffectiveEntryOpenDate = status.EffectiveEntryOpenDate,
                    EffectiveCutoffDate = status.EffectiveCutoffDate,
                    EffectiveEntryOpenDay = status.EffectiveEntryOpenDay,
                    EffectiveCutoffDay = status.EffectiveCutoffDay,
                    EffectiveReminderDaysBeforeCutoff = status.EffectiveReminderDaysBeforeCutoff,
                    HasExplicitOverride = status.HasExplicitOverride,
                    LockedAtUtc = status.LockedAtUtc,
                    UnlockedAtUtc = status.UnlockedAtUtc
                }
            };
        }
    }
}
