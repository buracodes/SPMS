using Application.CQRS.Initiative.Requests.Queries;
using Application.Dashboard;
using Application.DTOS.Initiative;
using Application.Interface.IRepository;
using Domain.Calculation;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Queries;

/// <summary>
/// Initiative grid projection. Filtering, sorting and paging run on the server and the milestone
/// aggregates arrive in a single grouped query, so the list costs the same number of round trips
/// regardless of page size. Progress and budget figures are calculated here, not in Angular.
/// </summary>
public class GetAllInitiativeQueryHandler : IRequestHandler<GetAllInitiativeQuery, PagedInitiativeDto>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeProgressCalculator progressCalculator;
    private readonly IInitiativeBudgetCalculator budgetCalculator;

    public GetAllInitiativeQueryHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeProgressCalculator _progressCalculator,
        IInitiativeBudgetCalculator _budgetCalculator)
    {
        initiativeRepository = _initiativeRepository;
        progressCalculator = _progressCalculator;
        budgetCalculator = _budgetCalculator;
    }

    public async Task<PagedInitiativeDto> Handle(GetAllInitiativeQuery request, CancellationToken cancellationToken)
    {
        var query = new InitiativeListQuery
        {
            Search = request.Search,
            Status = request.Status,
            Priority = request.Priority,
            OwnerId = request.OwnerId,
            IncludeClosed = request.IncludeClosed,
            Page = request.Page,
            PageSize = request.PageSize,
            Paged = request.Paged
        };

        var page = await initiativeRepository.SearchAsync(query, cancellationToken);
        var initiativeIds = page.Items.Select(i => i.Id).ToList();
        var inputs = await initiativeRepository.GetProgressInputsAsync(initiativeIds, cancellationToken);
        var today = DateTime.UtcNow.Date;

        var items = page.Items.Select(row =>
        {
            inputs.TryGetValue(row.Id, out var milestones);
            var progress = progressCalculator.Calculate(milestones ?? Array.Empty<InitiativeProgressInput>());
            var budget = budgetCalculator.Calculate(row.Budget, row.ActualExpenditure);

            return new GetAllInitiativeDto
            {
                Id = row.Id,
                Name = row.Name,
                Priority = row.Priority,
                Status = row.Status,
                Budget = row.Budget,
                ActualExpenditure = row.ActualExpenditure,
                OwnerId = row.OwnerId,
                OwnerName = row.OwnerName,
                GoalId = row.GoalId,
                KraId = row.KraId,
                KpiId = row.KpiId,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                MilestoneCount = row.MilestoneCount,
                ProgressPercentage = progress.ProgressPercentage,
                TotalMilestoneWeight = progress.TotalWeight,
                MilestoneWeightsAreValid = progress.WeightsAreValid,
                BudgetPosition = new InitiativeBudgetDto
                {
                    IsBudgetTracked = budget.IsBudgetTracked,
                    PlannedBudget = budget.PlannedBudget,
                    ActualExpenditure = budget.ActualExpenditure,
                    BudgetVariance = budget.BudgetVariance,
                    BudgetUtilizationPercent = budget.BudgetUtilizationPercent,
                    IsOverBudget = budget.IsOverBudget
                },
                IsOverdue = IsOverdue(row.Status, row.EndDate, today),
                ProgressRagStatus = RagFor(progress)
            };
        }).ToList();

        return new PagedInitiativeDto
        {
            Items = items,
            TotalCount = page.TotalCount,
            Page = query.Page,
            PageSize = query.Paged ? query.PageSize : items.Count
        };
    }

    /// <summary>
    /// FRD RAG banding for the calculated progress figure, mirroring <see cref="InitiativeDtoFactory"/>.
    /// An initiative with no milestones has no meaningful progress and reports NoData rather than 0% Red.
    /// </summary>
    internal static string RagFor(InitiativeProgressResult progress) =>
        progress.HasMilestones ? DashboardRag.FromScore(progress.ProgressPercentage) : DashboardRag.NoData;

    internal static bool IsOverdue(Domain.Enums.InitiativeStatus status, DateTime? endDate, DateTime today)
    {
        if (!endDate.HasValue)
        {
            return false;
        }

        var stillOpen = status is Domain.Enums.InitiativeStatus.NotStarted
            or Domain.Enums.InitiativeStatus.InProgress
            or Domain.Enums.InitiativeStatus.OnHold;

        return stillOpen && endDate.Value.Date < today;
    }
}
