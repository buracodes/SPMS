using Domain.Common;

namespace Domain.Entities;

public class DivisionGoal : CommonEntity
{
    public Guid DivisionPlanId { get; set; }
    public DivisionPlan? DivisionPlan { get; set; }

    public Guid ParentGoalId { get; set; }
    public Goal? ParentGoal { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    public decimal Weight { get; set; }

    public string? ContributionDescription { get; set; }

    public ICollection<Kpi> Kpis { get; set; } = new List<Kpi>();
}
