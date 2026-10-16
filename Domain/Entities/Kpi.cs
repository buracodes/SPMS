using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class Kpi : CommonEntity
{
    public Guid ScorecardId { get; set; }
    public Scorecard? Scorecard { get; set; }

    public Guid? KraId { get; set; }
    public Kra? Kra { get; set; }

    public Guid? DivisionGoalId { get; set; }
    public DivisionGoal? DivisionGoal { get; set; }

    public Guid? OwnerId { get; set; }
    public User? Owner { get; set; }

    public Guid? DivisionOwnerId { get; set; }
    public Division? DivisionOwner { get; set; }

    public Guid? UnitOfMeasureId { get; set; }
    public UnitsOfMeasure? UnitOfMeasure { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }

    public decimal Weight { get; set; }

    public decimal? ToleranceBandPercent { get; set; }

    public KpiOptimizationType OptimizationType { get; set; } = KpiOptimizationType.Maximize;
    public KpiDataSource DataSource { get; set; } = KpiDataSource.Manual;
    public KpiType KpiType { get; set; } = KpiType.Lagging;

    public bool IsCumulative { get; set; } = false;

    public Guid? ParentKpiId { get; set; }
    public Kpi? ParentKpi { get; set; }
    public ICollection<Kpi> ChildKpis { get; set; } = new List<Kpi>();

    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }

    public string? Formula { get; set; }

    public bool IsActive { get; set; } = true;
}
