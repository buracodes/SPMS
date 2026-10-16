using Domain.Entities;

namespace Application.Interface.IService;

public interface IFiscalCalendarService
{
    FiscalYear CreateFiscalYear(int startYear);
    IReadOnlyList<FiscalPeriod> GeneratePeriods(FiscalYear fiscalYear);
    int GetFiscalMonth(DateTime date, FiscalYear fiscalYear);
    int GetQuarter(int fiscalMonth);
}
