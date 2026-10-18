using Application.Interface.IService;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

/// <summary>
/// Shared period-scope resolution used by the scorecard and division score
/// calculation services so both honor the same YTD / Quarter / Month / Year rules.
/// </summary>
internal static class ScorePeriodScopeResolver
{
    public static ScorePeriodScope Resolve(
        PeriodType? periodType, int? quarter, int? fromMonth, int? toMonth, int currentFiscalMonth)
    {
        var type = periodType ?? PeriodType.Ytd;
        return type switch
        {
            PeriodType.Quarter =>
                new ScorePeriodScope(
                    (Math.Clamp(quarter ?? 1, 1, 4) - 1) * 3 + 1,
                    Math.Clamp(quarter ?? 1, 1, 4) * 3,
                    SnapshotGranularity.Quarter,
                    Math.Clamp(quarter ?? 1, 1, 4)),
            PeriodType.Month =>
                new ScorePeriodScope(
                    Math.Clamp(fromMonth ?? 1, 1, 12),
                    Math.Clamp(toMonth ?? fromMonth ?? 1, 1, 12),
                    SnapshotGranularity.Month,
                    0),
            PeriodType.Year =>
                new ScorePeriodScope(1, 12, SnapshotGranularity.Year, 1),
            _ =>
                new ScorePeriodScope(
                    1,
                    currentFiscalMonth <= 0 ? 12 : Math.Min(currentFiscalMonth, 12),
                    SnapshotGranularity.Ytd,
                    0)
        };
    }

    public static int GetCurrentFiscalMonth(FiscalYear fy, DateTime businessDate)
    {
        var now = businessDate.Date;
        if (now < fy.StartDate.Date) return 0;
        if (now > fy.EndDate.Date) return 12;

        var startMonth = fy.StartDate.Month;
        var currentMonth = now.Month;
        var startYear = fy.StartDate.Year;
        var currentYear = now.Year;

        int fiscalMonth = currentYear == startYear
            ? ((currentMonth - startMonth + 12) % 12) + 1
            : 12 + ((currentMonth - startMonth + 12) % 12) + 1;

        return Math.Min(fiscalMonth, 12);
    }
}
