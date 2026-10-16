using Domain.Common;

namespace Domain.Entities;

public class CoreValue : CommonEntity
{
    public Guid OrganizationProfileId { get; set; }
    public OrganizationProfile? OrganizationProfile { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
}
