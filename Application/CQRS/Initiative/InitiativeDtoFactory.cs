using Application.Dashboard;
using Application.DTOS.Initiative;
using Domain.Calculation;
using Domain.Entities;

namespace Application.CQRS.Initiative;

/// <summary>
/// Projects initiatives and milestones into DTOs, applying the authoritative progress and
/// budget calculators. Every initiative query/command goes through here so the calculated
/// figures can never differ between endpoints.
/// </summary>
public sealed class InitiativeDtoFactory
{
    private readonly IInitiativeProgressCalculator progressCalculator;
    private readonly IInitiativeBudgetCalculator budgetCalculator;
    private readonly Application.Interface.IService.IRagThresholdService ragThresholdService;

    public InitiativeDtoFactory(
        IInitiativeProgressCalculator progressCalculator,
        IInitiativeBudgetCalculator budgetCalculator,
        Application.Interface.IService.IRagThresholdService ragThresholdService)
    {
        this.progressCalculator = progressCalculator;
        this.budgetCalculator = budgetCalculator;
        this.ragThresholdService = ragThresholdService;
    }

    /// <summary>Effective RAG thresholds (FR-607) loaded once per factory instance (one per request).</summary>
    private RagThresholds? thresholds;

    private async Task<RagThresholds> ThresholdsAsync(CancellationToken cancellationToken)
        => thresholds ??= await ragThresholdService.GetEffectiveAsync(cancellationToken: cancellationToken);

    public async Task<InitiativeDto> BuildDetailAsync(Domain.Entities.Initiative initiative, CancellationToken cancellationToken = default)
    {
        var progress = progressCalculator.Calculate(ToProgressInputs(initiative.Milestones));
        var contributionByMilestone = progress.Contributions.ToDictionary(c => c.MilestoneId);
        var effectiveThresholds = await ThresholdsAsync(cancellationToken);

        return new InitiativeDto
        {
            Id = initiative.Id,
            Name = initiative.Name,
            Description = initiative.Description,
            GoalId = initiative.GoalId,
            GoalName = initiative.Goal?.Name,
            KraId = initiative.KraId,
            KraName = initiative.Kra?.Name,
            KpiId = initiative.KpiId,
            KpiName = initiative.Kpi?.Name,
            OwnerId = initiative.OwnerId,
            OwnerName = initiative.Owner?.FullName,
            Priority = initiative.Priority,
            Status = initiative.Status,
            StartDate = initiative.StartDate,
            EndDate = initiative.EndDate,
            Budget = initiative.Budget,
            ActualExpenditure = initiative.ActualExpenditure,
            Resources = initiative.Resources,
            IsClosed = initiative.IsClosed,
            ClosedAtUtc = initiative.ClosedAtUtc,
            ClosedByUserId = initiative.ClosedByUserId,
            ActualEndDate = initiative.ActualEndDate,
            LessonsLearned = initiative.LessonsLearned,
            OutcomeSummary = initiative.OutcomeSummary,
            ProgressPercentage = progress.ProgressPercentage,
            IsReadyForCompletion = IsReadyFor(progress),
            NotReadyReason = ReadinessBlock(progress),
            ProgressRagStatus = RagFor(progress, effectiveThresholds),
            TotalMilestoneWeight = progress.TotalWeight,
            MilestoneWeightsAreValid = progress.WeightsAreValid,
            BudgetPosition = BuildBudget(initiative.Budget, initiative.ActualExpenditure),
            Milestones = initiative.Milestones
                .OrderBy(m => m.DueDate)
                .ThenBy(m => m.Name)
                .Select(m => BuildMilestone(m, contributionByMilestone))
                .ToList(),
            Dependencies = initiative.Dependencies
                .Select(d => new InitiativeDependencyDto
                {
                    InitiativeId = d.InitiativeId,
                    DependsOnInitiativeId = d.DependsOnInitiativeId,
                    DependsOnInitiativeName = d.DependsOnInitiative?.Name
                })
                .OrderBy(d => d.DependsOnInitiativeName)
                .ToList()
        };
    }

    /// <summary>
    /// Compact projection used by create/update responses, where the milestone graph has just
    /// been written and the calculated figures are still wanted by the client.
    /// </summary>
    public async Task<InitiativeDto> BuildSummaryAsync(Domain.Entities.Initiative initiative, IEnumerable<Milestone>? milestones = null, CancellationToken cancellationToken = default)
    {
        var set = (milestones ?? initiative.Milestones ?? Array.Empty<Milestone>()).ToList();
        var progress = progressCalculator.Calculate(ToProgressInputs(set));
        var effectiveThresholds = await ThresholdsAsync(cancellationToken);

        return new InitiativeDto
        {
            Id = initiative.Id,
            Name = initiative.Name,
            Description = initiative.Description,
            GoalId = initiative.GoalId,
            KraId = initiative.KraId,
            KpiId = initiative.KpiId,
            OwnerId = initiative.OwnerId,
            Priority = initiative.Priority,
            Status = initiative.Status,
            StartDate = initiative.StartDate,
            EndDate = initiative.EndDate,
            Budget = initiative.Budget,
            ActualExpenditure = initiative.ActualExpenditure,
            Resources = initiative.Resources,
            IsClosed = initiative.IsClosed,
            ClosedAtUtc = initiative.ClosedAtUtc,
            ClosedByUserId = initiative.ClosedByUserId,
            ActualEndDate = initiative.ActualEndDate,
            LessonsLearned = initiative.LessonsLearned,
            OutcomeSummary = initiative.OutcomeSummary,
            ProgressPercentage = progress.ProgressPercentage,
            IsReadyForCompletion = IsReadyFor(progress),
            NotReadyReason = ReadinessBlock(progress),
            ProgressRagStatus = RagFor(progress, effectiveThresholds),
            TotalMilestoneWeight = progress.TotalWeight,
            MilestoneWeightsAreValid = progress.WeightsAreValid,
            BudgetPosition = BuildBudget(initiative.Budget, initiative.ActualExpenditure),
            Milestones = set
                .OrderBy(m => m.DueDate)
                .Select(m => BuildMilestone(m, null))
                .ToList()
        };
    }

    /// <summary>
    /// FRD lifecycle. The two readiness figures travel with the progress figure so the client can
    /// show "ready for completion" without duplicating the readiness rules in Angular.
    /// </summary>
    private static bool IsReadyFor(InitiativeProgressResult progress) =>
        InitiativeGuard.IsReadyForCompletion(progress);

    private static string? ReadinessBlock(InitiativeProgressResult progress) =>
        InitiativeGuard.DescribeWhyNotReadyForCompletion(progress);

    /// <summary>
    /// FRD RAG banding for the calculated progress figure. Delegates to <see cref="DashboardRag"/> so
    /// the configured thresholds (Green &gt;= 95%, Amber 70-94.9%, Red &lt; 70% by default) are applied
    /// everywhere the progress figure is shown. An initiative with no milestones has no meaningful
    /// progress, so it reports <see cref="DashboardRag.NoData"/> ("no score") rather than 0% Red.
    /// </summary>
    private static string RagFor(InitiativeProgressResult progress, RagThresholds thresholds) =>
        progress.HasMilestones
            ? DashboardRag.FromScore(progress.ProgressPercentage, thresholds)
            : DashboardRag.NoData;

    public MilestoneDto BuildMilestone(Milestone milestone, IReadOnlyDictionary<Guid, MilestoneContribution>? contributions = null)
    {
        var contribution = 0m;
        if (contributions is not null && contributions.TryGetValue(milestone.Id, out var found))
        {
            contribution = found.ContributionPercentage;
        }

        return new MilestoneDto
        {
            Id = milestone.Id,
            Name = milestone.Name,
            Description = milestone.Description,
            CompletionPercentage = milestone.CompletionPercentage,
            DueDate = milestone.DueDate,
            Weight = milestone.Weight,
            Status = milestone.Status,
            Comments = milestone.Comments,
            ContributionPercentage = contribution,
            UpdatedDate = milestone.UpdatedDate
        };
    }

    public InitiativeBudgetDto BuildBudget(decimal planned, decimal actual)
    {
        var result = budgetCalculator.Calculate(planned, actual);

        return new InitiativeBudgetDto
        {
            IsBudgetTracked = result.IsBudgetTracked,
            PlannedBudget = result.PlannedBudget,
            ActualExpenditure = result.ActualExpenditure,
            BudgetVariance = result.BudgetVariance,
            BudgetUtilizationPercent = result.BudgetUtilizationPercent,
            IsOverBudget = result.IsOverBudget
        };
    }

    public InitiativeUpdateLogDto BuildLog(InitiativeUpdateLog log) => new()
    {
        Id = log.Id,
        ChangeType = log.ChangeType,
        Details = log.Details,
        InitiativeId = log.InitiativeId,
        MilestoneId = log.MilestoneId,
        PreviousValue = log.PreviousValue,
        NewValue = log.NewValue,
        RecordedByUserId = log.CreatedBy == Guid.Empty ? null : log.CreatedBy,
        RecordedAtUtc = log.CreatedDate
    };

    /// <summary>
    /// Recalculates weighted progress from the initiative's currently loaded milestones, so status
    /// can be kept in step with progress without a second repository round trip.
    /// </summary>
    public InitiativeProgressResult BuildProgress(Domain.Entities.Initiative initiative) =>
        progressCalculator.Calculate(ToProgressInputs(initiative.Milestones));

    public static IReadOnlyList<InitiativeProgressInput> ToProgressInputs(IEnumerable<Milestone>? milestones) =>
        (milestones ?? Array.Empty<Milestone>())
            .Select(m => new InitiativeProgressInput(m.Id, m.Weight, m.CompletionPercentage))
            .ToList();
}
