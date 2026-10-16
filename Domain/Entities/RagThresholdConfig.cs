using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class RagThresholdConfig : CommonEntity
{
    public required string ScopeKey { get; set; }
    public RagBand Band { get; set; }
    public decimal? MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public required string Color { get; set; }
}
