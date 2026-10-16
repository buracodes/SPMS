namespace Application.DTOS.Goal;

public class CreateGoalDto
{
    public Guid Id { get; set; }
    public Guid ScorecardId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Weight { get; set; }
}
