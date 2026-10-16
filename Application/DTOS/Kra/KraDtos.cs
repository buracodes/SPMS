using Application.DTOS.CommonDto;

namespace Application.DTOS.Kra;

public class KraDto : CommonEntityDto
{
    public Guid Id { get; set; }
    public Guid GoalId { get; set; }
    public Guid ScorecardId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public Guid? StrategicThemeId { get; set; }
    public string? UnitMeasure { get; set; }
    public decimal Weight { get; set; }
    public bool IsActive { get; set; }
    public int KpiCount { get; set; }
}

public class CreateKraDto
{
    public Guid GoalId { get; set; }
    public Guid ScorecardId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public Guid? StrategicThemeId { get; set; }
    public string? UnitMeasure { get; set; }
    public decimal Weight { get; set; }
}

public class GetAllKraDto : CommonEntityDto
{
    public Guid Id { get; set; }
    public Guid GoalId { get; set; }
    public Guid ScorecardId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Weight { get; set; }
    public bool IsActive { get; set; }
    public int KpiCount { get; set; }

    /// <summary>Display names resolved server side so grids never have to show a guid.</summary>
    public string? ScorecardName { get; set; }
    public string? GoalName { get; set; }
}
