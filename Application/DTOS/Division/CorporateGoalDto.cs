namespace Application.DTOS.Division;

public class CorporateGoalDto
{
    public Guid GoalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Weight { get; set; }
}
