using Domain.Enums;

namespace Application.DTOS.Initiative;

public class InitiativeDto
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid? GoalId { get; set; }

    public string? GoalName { get; set; }

    public Guid? KraId { get; set; }

    public string? KraName { get; set; }

    public Guid? KpiId { get; set; }

    public string? KpiName { get; set; }

    public Guid OwnerId { get; set; }

    public string? OwnerName { get; set; }

    public InitiativePriority Priority { get; set; }

    public InitiativeStatus Status { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public decimal Budget { get; set; }

    public decimal ActualExpenditure { get; set; }

    public string? Resources { get; set; }

    public bool IsClosed { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public Guid? ClosedByUserId { get; set; }

    public DateTime? ActualEndDate { get; set; }

    public string? LessonsLearned { get; set; }

    public string? OutcomeSummary { get; set; }
    
    public decimal ProgressPercentage { get; set; }

    /// <summary>
    /// FRD lifecycle: true when calculated progress has reached 100% on a configured plan, so the
    /// initiative is waiting only on an authorised completion decision. Distinct from
    /// <see cref="Status"/>: readiness never sets <c>Completed</c> by itself.
    /// </summary>
    public bool IsReadyForCompletion { get; set; }

    /// <summary>
    /// FRD lifecycle: why this initiative cannot be completed yet, or null when it can. Lets the
    /// client explain the block instead of recomputing the readiness rules.
    /// </summary>
    public string? NotReadyReason { get; set; }

    /// <summary>
    /// Text RAG label ("Green" | "Yellow" | "Red") banding the calculated progress, matching the
    /// wire tokens dashboards and reports use. "Gray" when the initiative has no milestones to weigh.
    /// </summary>
    public string ProgressRagStatus { get; set; } = Application.Dashboard.DashboardRag.NoData;

    public decimal TotalMilestoneWeight { get; set; }

    public bool MilestoneWeightsAreValid { get; set; }

    public InitiativeBudgetDto BudgetPosition { get; set; } = new();

    public List<MilestoneDto> Milestones { get; set; } = new();

    public List<InitiativeDependencyDto> Dependencies { get; set; } = new();
}

/// <summary>FR-404 budget position, calculated by <see cref="Domain.Calculation.IInitiativeBudgetCalculator"/>.</summary>
public class InitiativeBudgetDto
{
    /// <summary>False when no planned budget is configured.</summary>
    public bool IsBudgetTracked { get; set; }

    public decimal PlannedBudget { get; set; }

    public decimal ActualExpenditure { get; set; }

    /// <summary>Planned - Actual. Negative means overspend. Null when no budget is tracked.</summary>
    public decimal? BudgetVariance { get; set; }

    /// <summary>Actual / Planned x 100, or null when no budget is tracked.</summary>
    public decimal? BudgetUtilizationPercent { get; set; }

    public bool IsOverBudget { get; set; }
}

public class MilestoneDto
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int CompletionPercentage { get; set; }

    public DateTime DueDate { get; set; }

    public decimal Weight { get; set; }

    /// <summary>Delivery state (FR-402). Forced to Completed at 100% completion (FR-403).</summary>
    public MilestoneStatus Status { get; set; }

    /// <summary>Most recent progress note.</summary>
    public string? Comments { get; set; }

    /// <summary>This milestone's contribution to the initiative's progress.</summary>
    public decimal ContributionPercentage { get; set; }

    public DateTime? UpdatedDate { get; set; }
}

/// <summary>FR-403 progress history entry.</summary>
public class InitiativeUpdateLogDto
{
    public Guid Id { get; set; }

    public required string ChangeType { get; set; }

    public string? Details { get; set; }

    public Guid InitiativeId { get; set; }

    public Guid? MilestoneId { get; set; }

    public string? MilestoneName { get; set; }

    public string? PreviousValue { get; set; }

    public string? NewValue { get; set; }

    public Guid? RecordedByUserId { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}

public class InitiativeDependencyDto
{
    public Guid InitiativeId { get; set; }

    public Guid DependsOnInitiativeId { get; set; }

    public string? DependsOnInitiativeName { get; set; }
}
