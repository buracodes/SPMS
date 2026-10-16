namespace Domain.Interfaces;

public interface IQuarterPeriodsStrategy
{
    int[] GetMonths(int quarter, DateTime fiscalYearStart);
}