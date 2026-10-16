using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class Initiative : CommonEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid? GoalId { get; set; }
    public Goal? Goal { get; set; }

    public Guid? KraId { get; set; }
    public Kra? Kra { get; set; }

    public Guid? KpiId { get; set; }
    public Kpi? Kpi { get; set; }

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public InitiativePriority Priority { get; set; } = InitiativePriority.Medium;

/// <summary>
    /// Deliberately hides <see cref="CommonEntity.Status"/>: for an initiative "status" is the
/// execution state (InitiativeStatus), not the generic EntityStatus lifecycle flag.
    /// A new initiative starts at <see cref="InitiativeStatus.NotStarted"/>.
    /// </summary>
    public new InitiativeStatus Status { get; set; } = InitiativeStatus.NotStarted;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    /// <summary>Planned budget. Zero means "budget tracking not configured" (FR-404).</summary>
    public decimal Budget { get; set; }

    /// <summary>Actual expenditure to date. Never negative; variance/utilisation derive from it.</summary>
    public decimal ActualExpenditure { get; set; }

    public string? Resources { get; set; }

    /// <summary>FR-405: set once closure information has been captured. Makes the initiative read-only.</summary>
    public bool IsClosed { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public Guid? ClosedByUserId { get; set; }

    public DateTime? ActualEndDate { get; set; }

    public string? LessonsLearned { get; set; }

    public string? OutcomeSummary { get; set; }

    public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();

    public ICollection<InitiativeUpdateLog> UpdateLogs { get; set; } = new List<InitiativeUpdateLog>();

    public ICollection<InitiativeDependency> Dependencies { get; set; } = new List<InitiativeDependency>();

    public ICollection<InitiativeDependency> Dependents { get; set; } = new List<InitiativeDependency>();
}
