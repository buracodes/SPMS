using Application.Exceptions;

namespace Application.CQRS.KpiTargets;

/// <summary>
/// FR-304: reconciliation of the monthly, quarterly and annual targets of one KPI
/// (corporate or division). Shared by the corporate target-configuration page and the
/// division plan module so a division KPI is configured identically:
/// <list type="bullet">
/// <item>cumulative KPIs: the monthly targets of each quarter must sum to that quarter's
/// target and the four quarterly targets must sum to the annual target;</item>
/// <item>non-cumulative KPIs: every monthly, quarterly and annual target must equal the
/// annual target.</item>
/// </list>
/// Comparisons tolerate a 0.005 rounding delta. Quarter-end/year-end anchoring is
/// validated by the caller, which owns the period selection.
/// </summary>
public static class KpiTargetReconciliation
{
    public const decimal Tolerance = 0.005m;

    public static void Validate(
        bool isCumulative,
        IReadOnlyDictionary<int, decimal> monthlyByMonth,
        IReadOnlyDictionary<int, decimal> quarterlyByQuarter,
        decimal annual)
    {
        if (isCumulative)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                if (!quarterlyByQuarter.TryGetValue(quarter, out var quarterly))
                {
                    throw new BadRequestException("All four quarterly targets must be filled.");
                }

                decimal monthlySum = 0m;
                for (var month = quarter * 3 - 2; month <= quarter * 3; month++)
                {
                    if (!monthlyByMonth.TryGetValue(month, out var monthly))
                    {
                        throw new BadRequestException("All monthly targets within each quarter must be filled.");
                    }
                    monthlySum += monthly;
                }

                if (Math.Abs(monthlySum - quarterly) >= Tolerance)
                {
                    throw new BadRequestException(
                        $"The monthly targets for Q{quarter} total {monthlySum:0.##} but the quarterly target is {quarterly:0.##}. They must match.");
                }
            }

            var quarterlySum = quarterlyByQuarter.Values.Sum();
            if (Math.Abs(quarterlySum - annual) >= Tolerance)
            {
                throw new BadRequestException(
                    $"The quarterly targets total {quarterlySum:0.##} but the annual target is {annual:0.##}. They must match.");
            }
        }
        else
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                if (!quarterlyByQuarter.TryGetValue(quarter, out var quarterly) ||
                    Math.Abs(quarterly - annual) >= Tolerance)
                {
                    throw new BadRequestException(
                        $"Each quarterly target must equal the annual target ({annual:0.##}) for a non-cumulative KPI.");
                }
            }

            for (var month = 1; month <= 12; month++)
            {
                if (!monthlyByMonth.TryGetValue(month, out var monthly) ||
                    Math.Abs(monthly - annual) >= Tolerance)
                {
                    throw new BadRequestException(
                        $"Each monthly target must equal the annual target ({annual:0.##}) for a non-cumulative KPI.");
                }
            }
        }
    }

    /// <summary>FR-304: quarterly targets follow from the monthly targets when no quarter row is explicitly configured.</summary>
    public static IReadOnlyDictionary<int, decimal> DeriveQuarterly(
        bool isCumulative,
        IReadOnlyDictionary<int, decimal> monthlyByMonth,
        decimal annual)
    {
        var derived = new Dictionary<int, decimal>();
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            if (isCumulative)
            {
                decimal sum = 0m;
                for (var month = quarter * 3 - 2; month <= quarter * 3; month++)
                {
                    sum += monthlyByMonth[month];
                }
                derived[quarter] = sum;
            }
            else
            {
                derived[quarter] = annual;
            }
        }

        return derived;
    }
}
