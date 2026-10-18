using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services
{
    /// <summary>
    /// Centralized year-end closure rule (FR-703). A scorecard is considered closed
    /// (read-only) when its own status is Closed or its fiscal year is no longer Open.
    /// Confirmed business rule: the closing boundary is the final-month grace period
    /// <see cref="FiscalYear.FinalMonthGraceDeadline"/> — actual entry stays open for
    /// seven calendar days after the fiscal year's end date (inclusive) — so a fiscal
    /// year whose grace has expired in ECX business time (East Africa Time) is treated
    /// as closed even before the nightly closure job flips its status. Every
    /// scorecard-owned write path funnels through this guard so the closure check is
    /// defined exactly once instead of being scattered across handlers, which also
    /// means the deadline is enforced on the relevant API operations and never
    /// depends on the scheduled job alone.
    /// </summary>
    public class ScorecardClosureGuard
    {
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IBusinessClock businessClock;

        public ScorecardClosureGuard(
            IScorecardRepository _scorecardRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IBusinessClock _businessClock)
        {
            scorecardRepository = _scorecardRepository;
            fiscalYearRepository = _fiscalYearRepository;
            businessClock = _businessClock;
        }

        /// <summary>
        /// True once the fiscal year's final-month grace period has expired in ECX business
        /// time — normal entry is blocked from the day after
        /// <see cref="FiscalYear.FinalMonthGraceDeadline"/>. Dynamic: derived from the
        /// year's configured end date, with no month-of-year assumption.
        /// </summary>
        public static bool YearHasEnded(FiscalYear year, DateTime businessDate)
        {
            return year.IsPastFinalMonthGrace(businessDate);
        }

        public static bool IsClosed(Scorecard scorecard, DateTime businessDate)
        {
            if (scorecard.ScorecardStatus == ScorecardStatus.Closed)
            {
                return true;
            }

            if (scorecard.FiscalYear is not null)
            {
                if (scorecard.FiscalYear.FiscalYearStatus != FiscalYearStatus.Open)
                {
                    return true;
                }

                if (YearHasEnded(scorecard.FiscalYear, businessDate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Throws <see cref="ScorecardClosedException"/> when the scorecard (or its fiscal
        /// year) is closed. Missing entities are ignored so the owning handler reports its
        /// usual not-found error with the same status code.
        /// </summary>
        public async Task EnsureScorecardOpenAsync(Guid scorecardId, CancellationToken cancellationToken = default)
        {
            var scorecard = await scorecardRepository.GetByIdWithFiscalYearAsync(scorecardId, cancellationToken);
            if (scorecard is null)
            {
                return;
            }

            if (IsClosed(scorecard, businessClock.BusinessDate))
            {
                throw new ScorecardClosedException(
                    scorecardId: scorecard.Id,
                    message: $"The scorecard '{scorecard.Name}' is closed and read-only; changes are not permitted.");
            }
        }

        /// <summary>
        /// Throws <see cref="ScorecardClosedException"/> when the fiscal year is no longer
        /// Open or its final-month grace period has expired (e.g. creating or cloning a
        /// scorecard into a closed year).
        /// </summary>
        public async Task EnsureFiscalYearOpenAsync(Guid fiscalYearId, CancellationToken cancellationToken = default)
        {
            var year = await fiscalYearRepository.GetById(fiscalYearId);
            if (year is null)
            {
                return;
            }

            if (year.FiscalYearStatus != FiscalYearStatus.Open || YearHasEnded(year, businessClock.BusinessDate))
            {
                throw new ScorecardClosedException(
                    fiscalYearId: year.Id,
                    message: $"The fiscal year '{year.Label}' is closed; scorecards can no longer be created in it.");
            }
        }
    }
}