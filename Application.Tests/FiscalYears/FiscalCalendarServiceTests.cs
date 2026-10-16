using Application.Services;
using Domain.Entities;

namespace Application.Tests.FiscalYears;

public class FiscalCalendarServiceTests
{
    [Fact]
    public void CreateFiscalYear_UsesGregorianJulyToJuneBoundary()
    {
        var service = new FiscalCalendarService();

        var fiscalYear = service.CreateFiscalYear(2025);

        Assert.Equal("2025/26", fiscalYear.Label);
        Assert.Equal(new DateTime(2025, 7, 1), fiscalYear.StartDate);
        Assert.Equal(new DateTime(2026, 6, 30), fiscalYear.EndDate);
    }

    [Theory]
    [InlineData(2025, 7, 1, 1)]
    [InlineData(2025, 12, 31, 6)]
    [InlineData(2026, 1, 1, 7)]
    [InlineData(2026, 6, 30, 12)]
    public void GetFiscalMonth_MapsJulyThroughJuneToOneThroughTwelve(int year, int month, int day, int expectedFiscalMonth)
    {
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);

        var fiscalMonth = service.GetFiscalMonth(new DateTime(year, month, day), fiscalYear);

        Assert.Equal(expectedFiscalMonth, fiscalMonth);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(12, 4)]
    public void GetQuarter_DerivesQuarterFromFiscalMonth(int fiscalMonth, int expectedQuarter)
    {
        var service = new FiscalCalendarService();

        var quarter = service.GetQuarter(fiscalMonth);

        Assert.Equal(expectedQuarter, quarter);
    }

    [Fact]
    public void GeneratePeriods_CreatesTwelveMonthlyPeriods()
    {
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);

        var periods = service.GeneratePeriods(fiscalYear);

        Assert.Equal(12, periods.Count);
        Assert.Equal(new DateTime(2025, 7, 1), periods[0].StartDate);
        Assert.Equal(new DateTime(2025, 7, 31), periods[0].EndDate);
        Assert.Equal(1, periods[0].FiscalMonth);
        Assert.Equal(1, periods[0].Quarter);
        Assert.Equal(new DateTime(2026, 6, 1), periods[11].StartDate);
        Assert.Equal(new DateTime(2026, 6, 30), periods[11].EndDate);
        Assert.Equal(12, periods[11].FiscalMonth);
        Assert.Equal(4, periods[11].Quarter);
    }

    [Fact]
    public void GeneratePeriods_FlagsQuarterEndAndYearEnd()
    {
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);

        var periods = service.GeneratePeriods(fiscalYear);

        Assert.Equal(new[] { 3, 6, 9, 12 }, periods.Where(p => p.IsQuarterEnd).Select(p => p.FiscalMonth).ToArray());
        Assert.Equal(12, periods.Single(p => p.IsYearEnd).FiscalMonth);
    }

    [Theory]
    [InlineData(2025, 7, 1, 2026, 6, 30)]
    [InlineData(2026, 9, 1, 2027, 8, 31)]
    [InlineData(2026, 1, 1, 2026, 12, 31)]
    [InlineData(2024, 2, 1, 2025, 1, 31)]
    [InlineData(2025, 3, 1, 2026, 2, 28)]
    [InlineData(2023, 3, 1, 2024, 2, 29)]
    public void ComputeEndDate_ReturnsLastDayOfTwelfthMonth(int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay)
    {
        var service = new FiscalCalendarService();

        var end = service.ComputeEndDate(new DateTime(startYear, startMonth, startDay));

        Assert.Equal(new DateTime(endYear, endMonth, endDay), end);
    }

    [Fact]
    public void GeneratePeriods_ForSeptemberStart_CreatesTwelveConsecutiveMonths()
    {
        var service = new FiscalCalendarService();
        var startDate = new DateTime(2026, 9, 1);
        var fiscalYear = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "2026/27",
            StartDate = startDate,
            EndDate = service.ComputeEndDate(startDate)
        };

        var periods = service.GeneratePeriods(fiscalYear);

        Assert.Equal(12, periods.Count);
        Assert.Equal(new DateTime(2026, 9, 1), periods[0].StartDate);
        Assert.Equal(new DateTime(2026, 9, 30), periods[0].EndDate);
        Assert.Equal(1, periods[0].FiscalMonth);
        Assert.Equal(new DateTime(2027, 8, 1), periods[11].StartDate);
        Assert.Equal(new DateTime(2027, 8, 31), periods[11].EndDate);
        Assert.Equal(12, periods[11].FiscalMonth);
        Assert.Equal(4, periods[11].Quarter);

        // Every period starts the day after the previous one ended: no gap, no overlap.
        for (var i = 1; i < periods.Count; i++)
        {
            Assert.Equal(periods[i - 1].EndDate.AddDays(1), periods[i].StartDate);
        }
    }
}
