using Domain.Common;

namespace Domain.Entities;

public class UnitsOfMeasure : CommonEntity
{
    public required string Name { get; set; }
    public string? Symbol { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
