namespace Domain.Enums;

/// <summary>
/// Execution state of an initiative (FRD lifecycle). Deliberately a small, explicit set:
/// the model has no separate "planned" and no "archived" state, because an initiative that has
/// not started yet is <see cref="NotStarted"/> and an initiative nobody is working on is
/// <see cref="OnHold"/> or <see cref="Cancelled"/>.
/// <para>
/// The numeric values are persisted (the columns are integers), so they are pinned rather than
/// renumbered: see the lifecycle data migration that remaps the retired values.
/// </para>
/// </summary>
public enum InitiativeStatus
{
    /// <summary>Default for a new initiative. No delivery work has been reported yet.</summary>
    NotStarted = 1,

    InProgress = 2,

    /// <summary>Set by formal closure, never by a progress edit.</summary>
    Completed = 3,

    /// <summary>Deliberately paused. Also the landing place for the retired Archived value.</summary>
    OnHold = 4,

    Cancelled = 5
}