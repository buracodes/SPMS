using Domain.Common;

namespace Domain.Entities;

public class StrategicTheme : CommonEntity
{
    public Guid OrganizationProfileId { get; set; }
    public OrganizationProfile? OrganizationProfile { get; set; }
    public required string Title { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
}
