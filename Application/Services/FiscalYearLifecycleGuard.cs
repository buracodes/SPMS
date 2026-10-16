using Application.DTOS.FiscalYear;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

/// <summary>
/// FR-703 lifecycle rules for fiscal-year create/update/delete, plus the Module 3/6 calendar
/// rules that keep every fiscal year a dynamic, twelve-consecutive-month calendar rather than a
/// fixed July–June one. The controller writes directly to the repository (it bypasses the
/// MediatR closure pipeline), so this guard is where the closure enforcement is repeated for
/// year-level mutations:
/// <list type="bullet">
///   <item>A fiscal year always starts on the first day of a month and spans exactly twelve
///   consecutive calendar months; the end date is derived from the start and may not be
///   anything else.</item>
///   <item>Fiscal years may not overlap and their labels must be unique.</item>
///   <item>A year that is closed, archived, or whose end date has already passed is
///   immutable — it must remain a read-only historical record.</item>
///   <item>Dates cannot change once the period calendar has been generated (periods are
///   referenced by targets, actual values and period-lock configs).</item>
///   <item>A year carrying scorecards, targets or actual values cannot be deleted:
///   deleting it would destroy historical scorecards.</item>
/// </list>
/// </summary>
public class FiscalYearLifecycleGuard
{
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IFiscalCalendarService fiscalCalendarService;

    public FiscalYearLifecycleGuard(
        IFiscalPeriodRepository fiscalPeriodRepository,
        IScorecardRepository scorecardRepository,
        IKpiTargetRepository kpiTargetRepository,
        IKpiActualValueRepository kpiActualValueRepository,
        IFiscalYearRepository fiscalYearRepository,
        IFiscalCalendarService fiscalCalendarService)
    {
        this.fiscalPeriodRepository = fiscalPeriodRepository;
        this.scorecardRepository = scorecardRepository;
        this.kpiTargetRepository = kpiTargetRepository;
        this.kpiActualValueRepository = kpiActualValueRepository;
        this.fiscalYearRepository = fiscalYearRepository;
        this.fiscalCalendarService = fiscalCalendarService;
    }

    /// <summary>
    /// Guards <see cref="FiscalYearController"/> create and returns the authoritative end date
    /// (last day of the twelfth month) that the caller must persist. A new year is always
    /// created Open. The start must be the first day of a month, a client-supplied end date
    /// must match the twelve-month span, the label must be unique and the calendar must not
    /// overlap an existing year.
    /// </summary>
    public async Task<DateTime> EnsureCanCreateAsync(CreateFiscalYearDto dto, CancellationToken cancellationToken = default)
    {
        var start = dto.StartDate.Date;

        if (start.Day != 1)
        {
            throw new BadRequestException("Fiscal year start date must be the first day of a month.");
        }

        var expectedEnd = fiscalCalendarService.ComputeEndDate(start);

        if (dto.EndDate.HasValue && dto.EndDate.Value.Date != expectedEnd)
        {
            throw new BadRequestException(
                $"A fiscal year must span twelve consecutive months; a start of {start:yyyy-MM-dd} means the end date must be {expectedEnd:yyyy-MM-dd}.");
        }

        var label = dto.Label?.Trim() ?? string.Empty;
        if (label.Length == 0)
        {
            throw new BadRequestException("Fiscal year label is required.");
        }

        if (await fiscalYearRepository.ExistsWithLabelAsync(label, null, cancellationToken))
        {
            throw new BadRequestException($"A fiscal year labelled '{label}' already exists.");
        }

        if (await fiscalYearRepository.AnyOverlappingAsync(start, expectedEnd, null, cancellationToken))
        {
            throw new BadRequestException(
                $"A fiscal year already covers {start:yyyy-MM-dd} to {expectedEnd:yyyy-MM-dd}. Fiscal years cannot overlap.");
        }

        return expectedEnd;
    }

    /// <summary>
    /// Guards <see cref="FiscalYearController"/> update against changes that bypass
    /// closure enforcement or alter a calendar that is already in use.
    /// </summary>
    public async Task EnsureCanUpdateAsync(FiscalYear year, UpdateFiscalYearDto dto, DateTime businessDate, CancellationToken cancellationToken = default)
    {
        if (year.FiscalYearStatus != FiscalYearStatus.Open)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' is {year.FiscalYearStatus.ToString().ToLower()} and immutable; it can no longer be edited.");
        }

        if (year.EndDate.Date < businessDate.Date)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' ended on {year.EndDate:yyyy-MM-dd} and is read-only; it can no longer be edited.");
        }

        var newStartDate = (dto.StartDate ?? year.StartDate).Date;
        var newEndDate = (dto.EndDate ?? year.EndDate).Date;
        if (newStartDate > newEndDate)
        {
            throw new BadRequestException("Fiscal year start date must be on or before its end date.");
        }

        if (dto.Label != null)
        {
            var label = dto.Label.Trim();
            if (label.Length > 0 && label != year.Label
                && await fiscalYearRepository.ExistsWithLabelAsync(label, year.Id, cancellationToken))
            {
                throw new BadRequestException($"A fiscal year labelled '{label}' already exists.");
            }
        }

        var datesChanged = newStartDate != year.StartDate.Date || newEndDate != year.EndDate.Date;
        if (!datesChanged)
        {
            return;
        }

        // Dates are frozen as soon as the monthly calendar exists, because the periods are
        // referenced by targets, actual values and period-lock configs. This is checked before
        // the shape rules below so an in-use calendar is always rejected the same way.
        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(year.Id);
        if (periods.Count > 0)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' already has a generated period calendar; its start and end dates can no longer be changed.");
        }

        if (newStartDate.Day != 1)
        {
            throw new BadRequestException("Fiscal year start date must be the first day of a month.");
        }

        var expectedEnd = fiscalCalendarService.ComputeEndDate(newStartDate);
        if (newEndDate != expectedEnd)
        {
            throw new BadRequestException(
                $"A fiscal year must span twelve consecutive months; a start of {newStartDate:yyyy-MM-dd} means the end date must be {expectedEnd:yyyy-MM-dd}.");
        }

        if (await fiscalYearRepository.AnyOverlappingAsync(newStartDate, expectedEnd, year.Id, cancellationToken))
        {
            throw new BadRequestException(
                $"A fiscal year already covers {newStartDate:yyyy-MM-dd} to {expectedEnd:yyyy-MM-dd}. Fiscal years cannot overlap.");
        }
    }

    /// <summary>
    /// Guards <see cref="FiscalYearController"/> delete. Deleting a fiscal year is only
    /// allowed while the year is still Open, its end date has not passed, and it holds no
    /// scorecards, targets or actual values. Otherwise the delete would bypass closure
    /// enforcement or remove data that historical scorecards depend on.
    /// </summary>
    public async Task EnsureCanDeleteAsync(FiscalYear year, DateTime businessDate, CancellationToken cancellationToken = default)
    {
        if (year.FiscalYearStatus != FiscalYearStatus.Open)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' is {year.FiscalYearStatus.ToString().ToLower()} and immutable; it cannot be deleted.");
        }

        if (year.EndDate.Date < businessDate.Date)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' ended on {year.EndDate:yyyy-MM-dd} and is read-only; it cannot be deleted.");
        }

        var hasScorecards = await scorecardRepository.AnyForFiscalYearAsync(year.Id);
        if (hasScorecards)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' still contains scorecards; it cannot be deleted because doing so would affect historical scorecards.");
        }

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(year.Id);
        var periodIds = periods.Select(p => p.Id).ToList();
        if (periodIds.Count == 0)
        {
            return;
        }

        var hasTargets = await kpiTargetRepository.AnyForPeriodsAsync(periodIds);
        var hasActuals = await kpiActualValueRepository.AnyForPeriodsAsync(periodIds);
        if (hasTargets || hasActuals)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' contains KPI targets or actual values; it cannot be deleted because doing so would destroy historical data.");
        }
    }
}
