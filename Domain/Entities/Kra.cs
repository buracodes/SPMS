using Domain.Common;

namespace Domain.Entities;

public class Kra : CommonEntity
{
    public Guid GoalId { get; set; }
    public Goal? Goal { get; set; }

    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    public string? Icon { get; set; }

    public Guid? StrategicThemeId { get; set; }
    public StrategicTheme? StrategicTheme { get; set; }

    public string? UnitMeasure { get; set; }

    public decimal Weight { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Kpi> Kpis { get; set; } = new List<Kpi>();
}
