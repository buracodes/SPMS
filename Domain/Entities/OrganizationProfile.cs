using Domain.Common;

namespace Domain.Entities;

public class OrganizationProfile : CommonEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string Vision { get; set; } = string.Empty;
    public string Mission { get; set; } = string.Empty;

    public ICollection<CoreValue> CoreValues { get; set; } = new List<CoreValue>();
    public ICollection<StrategicTheme> StrategicThemes { get; set; } = new List<StrategicTheme>();
    public ICollection<SwotItem> SwotItems { get; set; } = new List<SwotItem>();
    public ICollection<PestelItem> PestelItems { get; set; } = new List<PestelItem>();
}
