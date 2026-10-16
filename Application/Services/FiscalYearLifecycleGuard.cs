using Application.DTOS.FiscalYear;
using Application.Exceptions;
using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

/// <summary>
/// FR-703 lifecycle rules for fiscal-year create/update/delete. The controller writes
/// directly to the repository (they bypass the MediatR closure pipeline), so this guard
/// is where the closure enforcement is repeated for year-level mutations:
/// <list type="bullet">
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

    public FiscalYearLifecycleGuard(
        IFiscalPeriodRepository fiscalPeriodRepository,
        IScorecardRepository scorecardRepository,
        IKpiTargetRepository kpiTargetRepository,
        IKpiActualValueRepository kpiActualValueRepository)
    {
        this.fiscalPeriodRepository = fiscalPeriodRepository;
        this.scorecardRepository = scorecardRepository;
        this.kpiTargetRepository = kpiTargetRepository;
        this.kpiActualValueRepository = kpiActualValueRepository;
    }

    /// <summary>
    /// Guards <see cref="FiscalYearController"/> create. Only basic shape validation is
    /// required — a new year is always created Open.
    /// </summary>
    public Task EnsureCanCreateAsync(CreateFiscalYearDto dto)
    {
        if (dto.StartDate.Date > dto.EndDate.Date)
        {
            throw new BadRequestException("Fiscal year start date must be on or before its end date.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Guards <see cref="FiscalYearController"/> update against changes that bypass
    /// closure enforcement or alter a calendar that is already in use.
    /// </summary>
    public async Task EnsureCanUpdateAsync(FiscalYear year, UpdateFiscalYearDto dto, DateTime businessDate)
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

        var newStartDate = dto.StartDate ?? year.StartDate;
        var newEndDate = dto.EndDate ?? year.EndDate;
        if (newStartDate.Date > newEndDate.Date)
        {
            throw new BadRequestException("Fiscal year start date must be on or before its end date.");
        }

        var datesChanged = newStartDate.Date != year.StartDate.Date || newEndDate.Date != year.EndDate.Date;
        if (!datesChanged)
        {
            return;
        }

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(year.Id);
        if (periods.Count > 0)
        {
            throw new FiscalYearMutationException(
                year.Id,
                $"Fiscal year '{year.Label}' already has a generated period calendar; its start and end dates can no longer be changed.");
        }
    }

    /// <summary>
    /// Guards <see cref="FiscalYearController"/> delete. Deleting a fiscal year is only
    /// allowed while the year is still Open, its end date has not passed, and it holds no
    /// scorecards, targets or actual values. Otherwise the delete would bypass closure
    /// enforcement or remove data that historical scorecards depend on.
    /// </summary>
    public async Task EnsureCanDeleteAsync(FiscalYear year, DateTime businessDate)
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