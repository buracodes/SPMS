using Application.DTOS.CommonDto;
using Domain.Enums;

namespace Application.DTOS.Kpi;

public class KpiDto : CommonEntityDto
{
    public Guid Id { get; set; }
    public Guid ScorecardId { get; set; }
    public Guid? KraId { get; set; }
    public Guid? DivisionGoalId { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? DivisionOwnerId { get; set; }
    public string? DivisionOwnerName { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal Weight { get; set; }
    public decimal? ToleranceBandPercent { get; set; }
    public KpiOptimizationType OptimizationType { get; set; }
    public KpiDataSource DataSource { get; set; }
    public KpiType KpiType { get; set; }
    public bool IsCumulative { get; set; }
    public Guid? ParentKpiId { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Formula { get; set; }
    public bool IsActive { get; set; }
    public int ChildKpiCount { get; set; }
}

public class CreateKpiDto
{
    public Guid ScorecardId { get; set; }
    public Guid? KraId { get; set; }
    public Guid? DivisionGoalId { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? DivisionOwnerId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal Weight { get; set; }
    public KpiOptimizationType OptimizationType { get; set; } = KpiOptimizationType.Maximize;
    public KpiDataSource DataSource { get; set; } = KpiDataSource.Manual;
    public KpiType KpiType { get; set; } = KpiType.Lagging;
    public bool IsCumulative { get; set; }
    public Guid? ParentKpiId { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Formula { get; set; }
}

public class GetAllKpiDto : CommonEntityDto
{
    public Guid Id { get; set; }
    public Guid ScorecardId { get; set; }
    public Guid? KraId { get; set; }
    public Guid? DivisionGoalId { get; set; }
    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public Guid? DivisionOwnerId { get; set; }
    public string? DivisionOwnerName { get; set; }
    public Guid? UnitOfMeasureId { get; set; }

    /// <summary>Display names resolved server side so grids never have to show a guid.</summary>
    public string? ScorecardName { get; set; }
    public Guid? GoalId { get; set; }
    public string? KraName { get; set; }
    public string? GoalName { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal Weight { get; set; }
    public decimal? ToleranceBandPercent { get; set; }
    public KpiType KpiType { get; set; }
    public KpiOptimizationType OptimizationType { get; set; }
    public KpiDataSource DataSource { get; set; }
    public bool IsCumulative { get; set; }
    public Guid? ParentKpiId { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Formula { get; set; }
    public bool IsActive { get; set; }
}
