using Domain.Enums;

namespace Application.DTOS.Scorecard;

public class UpdateScorecardDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public ScorecardScope? Scope { get; set; }
}
