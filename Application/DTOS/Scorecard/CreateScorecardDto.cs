namespace Application.DTOS.Scorecard;

public class CreateScorecardDto
{
    public Guid FiscalYearId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}
