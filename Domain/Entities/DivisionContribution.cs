using Domain.Common;

namespace Domain.Entities;

public class DivisionContribution : CommonEntity
{
    public Guid DivisionPlanId { get; set; }
    public DivisionPlan? DivisionPlan { get; set; }

    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid GoalId { get; set; }
    public Goal? Goal { get; set; }
}
