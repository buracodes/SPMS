namespace Application.DTOS.Division;

public class DivisionGoalDto
{
    public Guid Id { get; set; }
    public Guid DivisionPlanId { get; set; }
    public Guid ParentGoalId { get; set; }
    public string ParentGoalName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Weight { get; set; }
    public string? ContributionDescription { get; set; }
    public List<DivisionKpiDto> Kpis { get; set; } = new();

    public decimal? Score { get; set; }
    public string? Status { get; set; }
}
