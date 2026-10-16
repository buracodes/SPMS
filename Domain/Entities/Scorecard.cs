using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class Scorecard : CommonEntity
{
    public Guid FiscalYearId { get; set; }
    public FiscalYear? FiscalYear { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ScorecardScope Scope { get; set; }
    public ScorecardStatus ScorecardStatus { get; set; } = ScorecardStatus.Active;
    public ICollection<Goal> Goals { get; set; } = new List<Goal>();
    public ICollection<Kra> Kras { get; set; } = new List<Kra>();
    public ICollection<Kpi> Kpis { get; set; } = new List<Kpi>();
}
