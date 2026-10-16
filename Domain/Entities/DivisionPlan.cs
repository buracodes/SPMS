using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class DivisionPlan : CommonEntity
{
    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }

    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public DivisionPlanStatus PlanStatus { get; set; } = DivisionPlanStatus.Draft;

    public DateTime? FinalizedAtUtc { get; set; }

    public Guid? FinalizedByUserId { get; set; }

    public ICollection<DivisionContribution> Contributions { get; set; } = new List<DivisionContribution>();
    public ICollection<DivisionGoal> Goals { get; set; } = new List<DivisionGoal>();
}
