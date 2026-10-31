using Application.CQRS.Division.Requests.Commands;
using Application.Validation.Division;

namespace Application.Tests.Divisions;

/// <summary>
/// The database enforces CK_Kpis_Weight_Range (&gt; 0), so the API must reject a zero-weight
/// division KPI before EF turns it into a 500.
/// </summary>
public class DivisionKpiValidatorTests
{
    private static CreateDivisionKpiCommandRequest CreateRequest(decimal weight) => new()
    {
        DivisionId = Guid.NewGuid(),
        ScorecardId = Guid.NewGuid(),
        DivisionGoalId = Guid.NewGuid(),
        Name = "New Branches",
        Weight = weight
    };

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CreateDivisionKpi_ZeroOrNegativeWeight_IsInvalid(decimal weight)
    {
        var result = new CreateDivisionKpiValidator().Validate(CreateRequest(weight));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDivisionKpiCommandRequest.Weight));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100)]
    public void CreateDivisionKpi_PositiveWeight_IsValid(decimal weight)
    {
        var result = new CreateDivisionKpiValidator().Validate(CreateRequest(weight));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateDivisionKpi_ZeroWeight_IsInvalid()
    {
        var result = new UpdateDivisionKpiValidator().Validate(new UpdateDivisionKpiCommandRequest
        {
            DivisionKpiId = Guid.NewGuid(),
            Weight = 0
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDivisionKpiCommandRequest.Weight));
    }
}
