using Application.Interface.IService;
using Application.Jobs;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Jobs
{
    /// <summary>
    /// FR-703 automatic year-end closure. Runs on a configurable daily cron and closes every
    /// fiscal year whose end date has passed (the first day of the following fiscal year),
    /// marking the year and all of its scorecards read-only. The boundary is evaluated in
    /// ECX business time (East Africa Time, UTC+3): the cron runs daily at 03:15 UTC
    /// (= 06:15 EAT), which is before ECX work hours, and any year that ended mid-day is
    /// already treated as read-only by <see cref="ScorecardClosureGuard"/> the moment the
    /// business date moves past it — so the job only finalizes the status transition.
    /// Idempotent: already-closed years and scorecards are skipped, so a retried or
    /// re-run job changes nothing.
    /// </summary>
    public sealed class FiscalYearClosureJob : ISpmsJob
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FiscalYearClosureJob> _logger;
        private readonly IBusinessClock _businessClock;

        public FiscalYearClosureJob(
            IServiceScopeFactory scopeFactory,
            ILogger<FiscalYearClosureJob> logger,
            IBusinessClock businessClock)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _businessClock = businessClock;
        }

        public async Task ExecuteAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SpmsDbContext>();
            var businessDate = _businessClock.BusinessDate;

            var eligibleYears = await context.FiscalYears
                .Where(fy => fy.FiscalYearStatus == FiscalYearStatus.Open && fy.EndDate.Date < businessDate)
                .OrderBy(fy => fy.StartDate)
                .ToListAsync();

            foreach (var year in eligibleYears)
            {
                var openScorecards = await context.Scorecards
                    .Where(s => s.FiscalYearId == year.Id && s.ScorecardStatus != ScorecardStatus.Closed)
                    .ToListAsync();

                year.FiscalYearStatus = FiscalYearStatus.Closed;
                foreach (var scorecard in openScorecards)
                {
                    scorecard.ScorecardStatus = ScorecardStatus.Closed;
                }

                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Fiscal year closure: closed fiscal year {YearLabel} ({YearId}) and {ScorecardCount} scorecard(s).",
                    year.Label,
                    year.Id,
                    openScorecards.Count);
            }

            if (eligibleYears.Count > 0)
            {
                _logger.LogInformation(
                    "Fiscal year closure completed for {YearCount} completed fiscal year(s).",
                    eligibleYears.Count);
            }
        }
    }
}