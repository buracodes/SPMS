namespace Domain.Enums;

/// <summary>
/// Delivery state of a single initiative milestone (FR-402).
/// Distinct from <see cref="InitiativeStatus"/>: a milestone is Completed when it
/// reaches 100% completion, independent of the parent initiative's status.
/// <para>
/// The numeric values are persisted (the columns are integers), so they are pinned rather than
/// renumbered: see the lifecycle data migration that remaps the retired value.
/// </para>
/// </summary>
public enum MilestoneStatus
{
    NotStarted = 1,

    InProgress = 2,

    /// <summary>Reached automatically at 100% completion.</summary>
    Completed = 3
}