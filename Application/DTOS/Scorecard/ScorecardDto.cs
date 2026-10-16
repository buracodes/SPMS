using Application.DTOS.CommonDto;
using Domain.Enums;

namespace Application.DTOS.Scorecard;

public class ScorecardDto : CommonEntityDto
{
    public Guid Id { get; set; }
    public Guid FiscalYearId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ScorecardScope Scope { get; set; }
    public ScorecardStatus ScorecardStatus { get; set; }
    public int GoalCount { get; set; }
    public int KraCount { get; set; }
    public int KpiCount { get; set; }
}
