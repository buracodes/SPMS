namespace Application.DTOS.Scorecard;

public class CloneScorecardResultDto
{
    public Guid SourceScorecardId { get; set; }
    public Guid TargetScorecardId { get; set; }
    public Guid TargetFiscalYearId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GoalCount { get; set; }
    public int KraCount { get; set; }
    public int KpiCount { get; set; }
    public int TargetCount { get; set; }
}
