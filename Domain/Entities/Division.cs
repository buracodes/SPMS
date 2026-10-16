using Domain.Common;

namespace Domain.Entities;

public class Division : CommonEntity
{
    public required string Name { get; set; }
    public required string Code { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<DivisionPlan> Plans { get; set; } = new List<DivisionPlan>();
}
