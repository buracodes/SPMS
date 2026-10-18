using Application.CQRS.PeriodLock.Requests.Queries;
using Application.DTOS.PeriodLock;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.PeriodLock.Handlers.Queries
{
    public class GetPeriodLockStatusQueryHandler : IRequestHandler<GetPeriodLockStatusQueryRequest, Response<GetPeriodLockStatusDto>>
    {
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IPeriodLockConfigRepository periodLockConfigRepository;
        private readonly IPeriodLockStatusService lockStatusService;
        private readonly IFiscalCalendarService fiscalCalendarService;

        public GetPeriodLockStatusQueryHandler(
            IScorecardRepository _scorecardRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IPeriodLockConfigRepository _periodLockConfigRepository,
            IPeriodLockStatusService _lockStatusService,
            IFiscalCalendarService _fiscalCalendarService)
        {
            scorecardRepository = _scorecardRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            fiscalYearRepository = _fiscalYearRepository;
            periodLockConfigRepository = _periodLockConfigRepository;
            lockStatusService = _lockStatusService;
            fiscalCalendarService = _fiscalCalendarService;
        }

        public async Task<Response<GetPeriodLockStatusDto>> Handle(GetPeriodLockStatusQueryRequest request, CancellationToken cancellationToken)
        {
            var scorecard = await scorecardRepository.GetById(request.ScorecardId);
            if (scorecard == null)
            {
                throw new NotFoundException(nameof(Scorecard), request.ScorecardId);
            }

            if (scorecard.FiscalYearId != request.FiscalYearId)
            {
                throw new BadRequestException("The requested fiscal year does not match the scorecard's fiscal year.");
            }

            var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(request.FiscalYearId);
            var fiscalYear = await fiscalYearRepository.GetById(request.FiscalYearId);

            // A GET must be side-effect free: if the year has no twelve-month calendar, fail
            // with 400 instead of silently generating (and persisting) the periods.
            if (fiscalYear is null || periods.Count != 12)
            {
                throw new BadRequestException("The fiscal year has no period calendar. Create the fiscal year periods before retrieving lock status.");
            }

            var configs = await periodLockConfigRepository.GetForScorecardFiscalYearAsync(request.ScorecardId, request.FiscalYearId);

            var statuses = await lockStatusService.GetStatusesAsync(request.ScorecardId, periods, configs);

            var items = new List<PeriodLockStatusItemDto>();
            for (var i = 0; i < periods.Count; i++)
            {
                var period = periods[i];
                var status = statuses[i];
                items.Add(new PeriodLockStatusItemDto
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
                });
            }

            return new Response<GetPeriodLockStatusDto>
            {
                Success = true,
                Message = "Period lock status retrieved successfully.",
                Data = new GetPeriodLockStatusDto
                {
                    ScorecardId = request.ScorecardId,
                    FiscalYearId = request.FiscalYearId,
                    Periods = items
                }
            };
        }
    }
}
