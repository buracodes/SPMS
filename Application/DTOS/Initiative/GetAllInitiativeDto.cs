using Domain.Enums;

namespace Application.DTOS.Initiative;

/// <summary>
/// Grid projection for the initiative list: only what the table renders. Progress and budget
/// figures are calculated on the backend so the grid never has to fetch milestone graphs.
/// </summary>
public class GetAllInitiativeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public InitiativePriority Priority { get; set; }
    public InitiativeStatus Status { get; set; }
    public decimal Budget { get; set; }
    public decimal ActualExpenditure { get; set; }
    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public Guid? GoalId { get; set; }
    public Guid? KraId { get; set; }
    public Guid? KpiId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedDate { get; set; }

    public bool IsClosed { get; set; }

    public int MilestoneCount { get; set; }

    /// <summary>Backend-calculated weighted progress (FR-403).</summary>
    public decimal ProgressPercentage { get; set; }

    /// <summary>FRD RAG band for the calculated progress figure (Green >= 95%, Amber 70-94.9%, Red < 70%), "NoData" when unmeasured.</summary>
    public string ProgressRagStatus { get; set; } = Application.Dashboard.DashboardRag.NoData;

    /// <summary>Sum of recorded milestone weights; 100 means properly configured (FR-402).</summary>
    public decimal TotalMilestoneWeight { get; set; }

    public bool MilestoneWeightsAreValid { get; set; }

    /// <summary>FR-404 variance and utilisation, calculated on the backend.</summary>
    public InitiativeBudgetDto BudgetPosition { get; set; } = new();

    /// <summary>End date passed while still open.</summary>
    public bool IsOverdue { get; set; }
}

/// <summary>Server-side paged envelope so the grid can avoid loading every initiative.</summary>
public class PagedInitiativeDto
{
    public List<GetAllInitiativeDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// Initiative portfolio metrics for dashboards/reports (FR reporting section). All figures are
/// produced by the backend progress and budget calculators.
/// </summary>
public class InitiativeSummaryDto
{
    public int TotalInitiatives { get; set; }
    public int NotStarted { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int OnHold { get; set; }
    public int Cancelled { get; set; }
    public int Closed { get; set; }

    /// <summary>Backend-calculated mean weighted progress across all initiatives (FR-403).</summary>
    public decimal AverageProgressPercentage { get; set; }

    /// <summary>Portfolio-wide actual / planned budget (FR-404). Null when nothing is funded.</summary>
    public decimal? BudgetUtilizationPercent { get; set; }

    public decimal TotalPlannedBudget { get; set; }
    public decimal TotalActualExpenditure { get; set; }
    public decimal TotalBudgetVariance { get; set; }

    public int OverdueInitiatives { get; set; }
    public int UpcomingMilestones { get; set; }
}
