using Application.DTOS.KpiTarget;
using Domain.Entities;
using System.Text.Json;

namespace Application.Tests.Kpis;

public class KpiTargetSerializationTests
{
    private static JsonSerializerOptions Options() => new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(TargetGranularity.Monthly, "Monthly")]
    [InlineData(TargetGranularity.Quarterly, "Quarterly")]
    [InlineData(TargetGranularity.Annual, "Annual")]
    public void Granularity_SerializesAsExactEnumName(TargetGranularity granularity, string expectedName)
    {
        var dto = new KpiTargetValueDto
        {
            FiscalMonth = 1,
            Quarter = 1,
            Granularity = granularity,
            TargetValue = 5
        };

        var json = JsonSerializer.Serialize(dto, Options());

        Assert.Contains($"\"granularity\":\"{expectedName}\"", json, StringComparison.Ordinal);
    }
}