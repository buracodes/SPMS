using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class SwotItem : CommonEntity
{
    public Guid OrganizationProfileId { get; set; }
    public OrganizationProfile? OrganizationProfile { get; set; }
    public SwotCategory Category { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; } = ImpactLevel.Medium;
}
