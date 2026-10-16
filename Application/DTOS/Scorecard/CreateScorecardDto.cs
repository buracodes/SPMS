using Domain.Enums;

namespace Application.DTOS.Scorecard;

public class CreateScorecardDto
{
    public Guid FiscalYearId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ScorecardScope Scope { get; set; } = ScorecardScope.Corporate;
}
