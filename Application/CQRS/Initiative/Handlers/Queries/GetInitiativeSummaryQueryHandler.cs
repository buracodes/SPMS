using Application.CQRS.Initiative.Requests.Queries;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface.IRepository;
using Domain.Calculation;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Queries;

/// <summary>
/// FR-403 progress history: every logged change for an initiative (optionally narrowed to one
/// milestone), newest first. Entries are never overwritten.
/// </summary>
public class GetInitiativeHistoryQueryHandler
    : IRequestHandler<GetInitiativeHistoryQueryRequest, List<InitiativeUpdateLogDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly InitiativeDtoFactory dtoFactory;

    public GetInitiativeHistoryQueryHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        InitiativeDtoFactory _dtoFactory)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        dtoFactory = _dtoFactory;
    }

    public async Task<List<InitiativeUpdateLogDto>> Handle(
        GetInitiativeHistoryQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (await initiativeRepository.GetById(request.InitiativeId) == null)
        {
            throw new NotFoundException("Initiative", request.InitiativeId);
        }

        var logs = await updateLogRepository.GetForInitiativeAsync(request.InitiativeId, request.MilestoneId);
        var milestones = await initiativeRepository.GetDetailAsync(request.InitiativeId);
        var milestoneNames = (milestones?.Milestones ?? new List<Domain.Entities.Milestone>())
            .ToDictionary(m => m.Id, m => m.Name);

        return logs.Select(log =>
        {
            var dto = dtoFactory.BuildLog(log);
            if (dto.MilestoneId.HasValue && milestoneNames.TryGetValue(dto.MilestoneId.Value, out var name))
            {
                dto.MilestoneName = name;
            }

            return dto;
        }).ToList();
    }
}

/// <summary>
/// Portfolio metrics for dashboards and reports. Every percentage is produced by the
/// authoritative progress and budget calculators rather than re-derived here.
/// </summary>
public class GetInitiativeSummaryQueryHandler : IRequestHandler<GetInitiativeSummaryQuery, InitiativeSummaryDto>
{
    private static readonly int UpcomingMilestoneWindowDays = 30;

    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeProgressCalculator progressCalculator;
    private readonly IInitiativeBudgetCalculator budgetCalculator;

    public GetInitiativeSummaryQueryHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeProgressCalculator _progressCalculator,
        IInitiativeBudgetCalculator _budgetCalculator)
    {
        initiativeRepository = _initiativeRepository;
        progressCalculator = _progressCalculator;
        budgetCalculator = _budgetCalculator;
    }

    public async Task<InitiativeSummaryDto> Handle(GetInitiativeSummaryQuery request, CancellationToken cancellationToken)
    {
        var initiatives = request.OwnerId.HasValue
            ? await initiativeRepository.GetByOwnerIdWithMilestonesAsync(request.OwnerId.Value)
            : await initiativeRepository.GetAllWithMilestonesAsync();

        var ids = initiatives.Select(i => i.Id).ToList();
        var inputs = await initiativeRepository.GetProgressInputsAsync(ids, cancellationToken);

        var today = DateTime.UtcNow.Date;
        var summary = new InitiativeSummaryDto();

        var progressValues = new List<decimal>();

        foreach (var initiative in initiatives)
        {
            inputs.TryGetValue(initiative.Id, out var milestones);
            var progress = progressCalculator.Calculate(milestones ?? Array.Empty<Domain.Calculation.InitiativeProgressInput>());
            progressValues.Add(progress.ProgressPercentage);

            switch (initiative.Status)
            {
                case Domain.Enums.InitiativeStatus.NotStarted:
                    summary.NotStarted++;
                    break;
                case Domain.Enums.InitiativeStatus.InProgress:
                    summary.InProgress++;
                    break;
                case Domain.Enums.InitiativeStatus.Completed:
                    summary.Completed++;
                    break;
                case Domain.Enums.InitiativeStatus.OnHold:
                    summary.OnHold++;
                    break;
                case Domain.Enums.InitiativeStatus.Cancelled:
                    summary.Cancelled++;
                    break;
            }

            if (initiative.IsClosed)
            {
                summary.Closed++;
            }

            summary.TotalPlannedBudget += initiative.Budget;
            summary.TotalActualExpenditure += initiative.ActualExpenditure;

            if (GetAllInitiativeQueryHandler.IsOverdue(initiative.Status, initiative.EndDate, today))
            {
                summary.OverdueInitiatives++;
            }

            if (initiative.Status is Domain.Enums.InitiativeStatus.Completed
                or Domain.Enums.InitiativeStatus.Cancelled)
            {
                continue;
            }

            var upcomingFrom = today.AddDays(1);
            var upcomingTo = today.AddDays(UpcomingMilestoneWindowDays);

            summary.UpcomingMilestones += initiative.Milestones.Count(m =>
                m.CompletionPercentage < 100
                && m.DueDate.Date >= upcomingFrom
                && m.DueDate.Date <= upcomingTo);
        }

        summary.TotalInitiatives = initiatives.Count;
        summary.AverageProgressPercentage = progressValues.Count == 0
            ? 0m
            : Math.Round(progressValues.Average(), 2, MidpointRounding.AwayFromZero);

        summary.TotalBudgetVariance = summary.TotalPlannedBudget - summary.TotalActualExpenditure;

        // Portfolio utilisation reuses the budget calculator so the divide-by-zero guard and the
        // rounding rules are identical to the per-initiative figures.
        summary.BudgetUtilizationPercent = budgetCalculator
            .Calculate(summary.TotalPlannedBudget, summary.TotalActualExpenditure)
            .BudgetUtilizationPercent;

        return summary;
    }
}
