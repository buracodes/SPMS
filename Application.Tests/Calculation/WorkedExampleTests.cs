using Domain.Calculation;
using Domain.Calculation.ScoreStrategies;
using Domain.Enums;

namespace Application.Tests.Calculation;

public class WorkedExampleTests
{
    [Fact]
    public void KraScore_WorkedExample_Produces82_5()
    {
        var factory = new ScoreStrategyFactory();
        var kpiCalculator = new KpiScoreCalculator(factory);
        var kraCalculator = new KraScoreCalculator();

        var kpiA = kpiCalculator.Calculate(new KpiScoreRequest(90, 100, KpiOptimizationType.Maximize));
        var kpiB = kpiCalculator.Calculate(new KpiScoreRequest(70, 100, KpiOptimizationType.Maximize));
        var kpiC = kpiCalculator.Calculate(new KpiScoreRequest(80, 100, KpiOptimizationType.Maximize));

        var contributions = new List<KraScoreContribution>
        {
            new(kpiA, 50m, KpiType.Lagging),
            new(kpiB, 30m, KpiType.Lagging),
            new(kpiC, 20m, KpiType.Leading)
        };

        var kra = kraCalculator.Calculate(contributions);

        Assert.NotNull(kra);
        Assert.Equal(82.5m, kra!.Value);
    }

    [Fact]
    public void KraScore_NoLaggingKpis_ReturnsNull()
    {
        var kraCalculator = new KraScoreCalculator();

        var contributions = new List<KraScoreContribution>
        {
            new(90m, 50m, KpiType.Leading),
            new(70m, 30m, KpiType.Leading)
        };

        Assert.Null(kraCalculator.Calculate(contributions));
    }

    [Fact]
    public void KraScore_LaggingKpiWithoutScore_ExcludedAndWeightsRenormalized()
    {
        var kraCalculator = new KraScoreCalculator();

        var contributions = new List<KraScoreContribution>
        {
            new(90m, 50m, KpiType.Lagging),
            new(null, 30m, KpiType.Lagging),
            new(70m, 30m, KpiType.Lagging)
        };

        var kra = kraCalculator.Calculate(contributions);

        Assert.NotNull(kra);
        Assert.Equal((90m * 50m + 70m * 30m) / 80m, kra!.Value);
    }
}
