using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class SystemConfig : CommonEntity
{
    public required string Key { get; set; }
    public required string ValueJson { get; set; }
    public SystemConfigCategory Category { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
