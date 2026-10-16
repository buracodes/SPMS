using Domain.Enums;

namespace Application.DTOS.Division;

public class DivisionPlanSummaryDto
{
    public Guid Id { get; set; }
    public Guid DivisionId { get; set; }
    public Guid ScorecardId { get; set; }
    public string ScorecardName { get; set; } = string.Empty;
    public Guid FiscalYearId { get; set; }
    public DivisionPlanStatus PlanStatus { get; set; }
    public int GoalCount { get; set; }
    public int KpiCount { get; set; }
}
