using Domain.Enums;
using Domain.Interfaces;

namespace Domain.Calculation.ScoreStrategies;

public class QuarterPeriodsStrategy : IQuarterPeriodsStrategy
{
    public int[] GetMonths(int quarter, DateTime fiscalYearStart)
    {
        if (quarter < 1 || quarter > 4)
            throw new ArgumentOutOfRangeException(nameof(quarter), "Quarter must be between 1 and 4");

        var startMonth = fiscalYearStart.Month;
        var months = new int[3];

        for (int i = 0; i < 3; i++)
        {
            var gregorianMonth = ((startMonth - 1 + ((quarter - 1) * 3 + i)) % 12) + 1;
            months[i] = gregorianMonth;
        }

        return months;
    }
}