using Domain.Entities;

namespace Application.Interface.IService;

public interface IFiscalCalendarService
{
    FiscalYear CreateFiscalYear(int startYear);

    /// <summary>
    /// The authoritative end date for a fiscal year that starts on <paramref name="startDate"/>:
    /// the last day of the twelfth consecutive calendar month (twelve months minus one day).
    /// </summary>
    DateTime ComputeEndDate(DateTime startDate);

    IReadOnlyList<FiscalPeriod> GeneratePeriods(FiscalYear fiscalYear);
    int GetFiscalMonth(DateTime date, FiscalYear fiscalYear);
    int GetQuarter(int fiscalMonth);
}
