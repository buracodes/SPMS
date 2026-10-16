using Domain.Enums;

namespace Application.DTOS.Division;

public class DivisionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DivisionPlanStatus? LatestPlanStatus { get; set; }
}
