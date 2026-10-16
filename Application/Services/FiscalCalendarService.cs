using Application.Interface.IService;
using Domain.Entities;

namespace Application.Services;

public sealed class FiscalCalendarService : IFiscalCalendarService
{
    public FiscalYear CreateFiscalYear(int startYear)
    {
        if (startYear < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startYear), "Fiscal year start year must be a valid Gregorian year.");
        }

        return new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = $"{startYear}/{(startYear + 1) % 100:00}",
            StartDate = new DateTime(startYear, 7, 1),
            EndDate = new DateTime(startYear + 1, 6, 30)
        };
    }

    public IReadOnlyList<FiscalPeriod> GeneratePeriods(FiscalYear fiscalYear)
    {
        if (fiscalYear.StartDate.Date > fiscalYear.EndDate.Date)
        {
            throw new ArgumentException("Fiscal year start date must be before or equal to end date.", nameof(fiscalYear));
        }

        var periods = new List<FiscalPeriod>(capacity: 12);
        for (var fiscalMonth = 1; fiscalMonth <= 12; fiscalMonth++)
        {
            var startDate = fiscalYear.StartDate.Date.AddMonths(fiscalMonth - 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            periods.Add(new FiscalPeriod
            {
                Id = Guid.NewGuid(),
                FiscalYearId = fiscalYear.Id,
                FiscalMonth = fiscalMonth,
                Quarter = GetQuarter(fiscalMonth),
                StartDate = startDate,
                EndDate = endDate,
                IsQuarterEnd = fiscalMonth % 3 == 0,
                IsYearEnd = fiscalMonth == 12
            });
        }

        return periods;
    }

    public int GetFiscalMonth(DateTime date, FiscalYear fiscalYear)
    {
        var normalizedDate = date.Date;
        if (normalizedDate < fiscalYear.StartDate.Date || normalizedDate > fiscalYear.EndDate.Date)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "Date is outside the fiscal year.");
        }

        return ((normalizedDate.Year - fiscalYear.StartDate.Year) * 12) + normalizedDate.Month - fiscalYear.StartDate.Month + 1;
    }

    public int GetQuarter(int fiscalMonth)
    {
        if (fiscalMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalMonth), "Fiscal month must be between 1 and 12.");
        }

        return ((fiscalMonth - 1) / 3) + 1;
    }
}
