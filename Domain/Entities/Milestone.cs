using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class Milestone : CommonEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public int CompletionPercentage { get; set; }

    public DateTime DueDate { get; set; }

    /// <summary>Relative weight of this milestone inside its initiative. All weights total 100.</summary>
    public decimal Weight { get; set; } = 1m;

    /// <summary>
    /// Delivery state. Automatically becomes <see cref="MilestoneStatus.Completed"/> at 100% (FR-403).
    /// Deliberately hides <see cref="CommonEntity.Status"/>, matching Initiative.
    /// </summary>
    public new MilestoneStatus Status { get; set; } = MilestoneStatus.NotStarted;

    /// <summary>Most recent progress note (FR-403). Full history lives in <see cref="InitiativeUpdateLog"/>.</summary>
    public string? Comments { get; set; }

    public Guid InitiativeId { get; set; }
    public Initiative Initiative { get; set; } = null!;
}
