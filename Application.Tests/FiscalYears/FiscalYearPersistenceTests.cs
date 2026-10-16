using Application.Services;
using Application.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.FiscalYears;

public class FiscalYearPersistenceTests
{
    [Fact]
    public async Task FiscalYear_WithGeneratedPeriods_PersistsTwelvePeriods()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var service = new FiscalCalendarService();
        var fiscalYear = service.CreateFiscalYear(2025);
        foreach (var period in service.GeneratePeriods(fiscalYear))
        {
            fiscalYear.Periods.Add(period);
        }

        lease.Context.FiscalYears.Add(fiscalYear);
        await lease.Context.SaveChangesAsync();

        var persistedFiscalYear = await lease.Context.FiscalYears
            .Include(fy => fy.Periods)
            .SingleAsync();

        Assert.Equal("2025/26", persistedFiscalYear.Label);
        Assert.Equal(12, persistedFiscalYear.Periods.Count);
        Assert.Contains(persistedFiscalYear.Periods, period =>
            period.FiscalMonth == 1
            && period.Quarter == 1
            && period.StartDate == new DateTime(2025, 7, 1)
            && period.EndDate == new DateTime(2025, 7, 31));
        Assert.Contains(persistedFiscalYear.Periods, period =>
            period.FiscalMonth == 12
            && period.Quarter == 4
            && period.StartDate == new DateTime(2026, 6, 1)
            && period.EndDate == new DateTime(2026, 6, 30));
    }
}
