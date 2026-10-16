using Application.DTOS.CommonDto;

namespace Application.DTOS.Goal;

public class GetAllGoalDto : CommonEntityDto
{
    public Guid Id { get; set; }
    public Guid ScorecardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Weight { get; set; }

    /// <summary>Display names resolved server side so grids never have to show a guid.</summary>
    public string? ScorecardName { get; set; }
    public string? FiscalYearName { get; set; }
}
