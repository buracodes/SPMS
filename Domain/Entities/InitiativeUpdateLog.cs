using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Append-only history of everything that happens to an initiative: creations, status
/// changes, milestone progress updates, budget changes, dependency changes and closure.
/// <see cref="CommonEntity.CreatedBy"/> records the acting user and
/// <see cref="CommonEntity.CreatedDate"/> the timestamp (FR-403), so this is the single
/// source of progress history — no separate progress-update table is kept.
/// </summary>
public class InitiativeUpdateLog : CommonEntity
{
    public required string ChangeType { get; set; }

    public string? Details { get; set; }

    public required Guid InitiativeId { get; set; }
    public Initiative Initiative { get; set; } = null!;

    /// <summary>Set when the entry describes a single milestone rather than the whole initiative.</summary>
    public Guid? MilestoneId { get; set; }
    public Milestone? Milestone { get; set; }

    /// <summary>Prior value of the tracked attribute, when applicable (e.g. "50% / InProgress").</summary>
    public string? PreviousValue { get; set; }

    /// <summary>New value of the tracked attribute, when applicable (e.g. "60% / InProgress").</summary>
    public string? NewValue { get; set; }
}
