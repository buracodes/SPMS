namespace Application.DTOS.Division;

public class DivisionKpiDto
{
    public Guid Id { get; set; }
    public Guid DivisionGoalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public Guid? UnitOfMeasureId { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? DivisionOwnerId { get; set; }
    public string? DivisionOwnerName { get; set; }
    public decimal Weight { get; set; }
    public decimal? ToleranceBandPercent { get; set; }
    public string OptimizationType { get; set; } = string.Empty;
    public string KpiType { get; set; } = string.Empty;
    public string DataSource { get; set; } = string.Empty;
    public bool IsCumulative { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Formula { get; set; }
    public bool IsActive { get; set; }

    public decimal? Score { get; set; }
    public decimal? Target { get; set; }
    public decimal? Actual { get; set; }
    public string? Status { get; set; }

    /// <summary>Persisted monthly/quarterly/annual targets (FR-904), used to reload the entry form.</summary>
    public List<DivisionKpiTargetDto> Targets { get; set; } = new();
}
