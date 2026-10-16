using Domain.Common;

namespace Domain.Entities;

public class Goal : CommonEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }
    public decimal Weight { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Kra> Kras { get; set; } = new List<Kra>();
}
