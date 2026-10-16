using Domain.Enums;

namespace Application.DTOS.Division;

public class DivisionPlanDto
{
    public Guid Id { get; set; }
    public Guid DivisionId { get; set; }
    public string DivisionName { get; set; } = string.Empty;
    public Guid ScorecardId { get; set; }
    public string ScorecardName { get; set; } = string.Empty;
    public Guid FiscalYearId { get; set; }
    public DivisionPlanStatus PlanStatus { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }

    /// <summary>Corporate scorecard this division scorecard is aligned to (same fiscal year).</summary>
    public Guid? CorporateScorecardId { get; set; }
    public string? CorporateScorecardName { get; set; }

    /// <summary>Weighted overall division score for the active period, or null when uncalculable.</summary>
    public decimal? OverallScore { get; set; }

    public List<CorporateGoalDto> Contributions { get; set; } = new();
    public List<CorporateGoalDto> AvailableGoals { get; set; } = new();
    public List<DivisionGoalDto> Goals { get; set; } = new();

    /// <summary>Fiscal periods of the plan's fiscal year (FR-904 target entry).</summary>
    public List<DivisionFiscalPeriodDto> FiscalPeriods { get; set; } = new();
}
