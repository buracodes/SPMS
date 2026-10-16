using Application.DTOS.Division;
using Application.Exceptions;
using Domain.Entities;

namespace Application.CQRS.Division;

/// <summary>
/// FR-904 / FR-304: validates and materialises the monthly, quarterly and annual targets of a
/// division KPI. The reconciliation rules mirror the corporate target-configuration page so a
/// division KPI is configured identically to a corporate KPI:
/// <list type="bullet">
/// <item>the annual target is required whenever any target is supplied;</item>
/// <item>cumulative KPIs: the monthly values of each quarter must sum to the quarterly target and
/// the four quarterly targets must sum to the annual target;</item>
/// <item>non-cumulative KPIs: every quarterly and monthly target must equal the annual target.</item>
/// </list>
/// An empty target collection is allowed and simply means "no targets configured yet".
/// </summary>
public static class DivisionKpiTargetBuilder
{
    private const decimal Tolerance = 0.005m;

    public static List<KpiTarget> Build(
        Guid kpiId,
        bool isCumulative,
        IReadOnlyList<DivisionKpiTargetInputDto>? targets,
        IReadOnlyList<FiscalPeriod> fiscalPeriods)
    {
        if (targets == null || targets.Count == 0)
        {
            return new List<KpiTarget>();
        }

        var periodById = fiscalPeriods.ToDictionary(p => p.Id);

        var seen = new HashSet<(Guid, TargetGranularity)>();
        var monthlyByMonth = new Dictionary<int, decimal>();
        var quarterlyByQuarter = new Dictionary<int, decimal>();
        decimal? annual = null;

        foreach (var target in targets)
        {
            if (!periodById.TryGetValue(target.FiscalPeriodId, out var period))
            {
                throw new BadRequestException("One or more target periods do not belong to the scorecard's fiscal year.");
            }

            if (!seen.Add((target.FiscalPeriodId, target.Granularity)))
            {
                throw new BadRequestException("Duplicate targets were supplied for the same period and granularity.");
            }

            switch (target.Granularity)
            {
                case TargetGranularity.Monthly:
                    monthlyByMonth[period.FiscalMonth] = target.TargetValue;
                    break;
                case TargetGranularity.Quarterly:
                    if (!period.IsQuarterEnd)
                    {
                        throw new BadRequestException("Quarterly targets must use a quarter-end fiscal period.");
                    }
                    quarterlyByQuarter[period.Quarter] = target.TargetValue;
                    break;
                case TargetGranularity.Annual:
                    if (!period.IsYearEnd)
                    {
                        throw new BadRequestException("The annual target must use the fiscal year-end period.");
                    }
                    annual = target.TargetValue;
                    break;
                default:
                    throw new BadRequestException("A target was supplied with an unknown granularity.");
            }
        }

        if (annual is null)
        {
            throw new BadRequestException("An annual target is required.");
        }

        ValidateReconciliation(isCumulative, monthlyByMonth, quarterlyByQuarter, annual.Value);

        var result = new List<KpiTarget>(targets.Count);
        foreach (var target in targets)
        {
            result.Add(new KpiTarget
            {
                Id = Guid.NewGuid(),
                KpiId = kpiId,
                FiscalPeriodId = target.FiscalPeriodId,
                TargetValue = target.TargetValue,
                Granularity = target.Granularity
            });
        }

        return result;
    }

    private static void ValidateReconciliation(
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
                if (!quarterlyByQuarter.TryGetValue(quarter, out var quarterly) || Math.Abs(quarterly - annual) >= Tolerance)
                {
                    throw new BadRequestException(
                        $"Each quarterly target must equal the annual target ({annual:0.##}) for a non-cumulative KPI.");
                }
            }

            for (var month = 1; month <= 12; month++)
            {
                if (!monthlyByMonth.TryGetValue(month, out var monthly) || Math.Abs(monthly - annual) >= Tolerance)
                {
                    throw new BadRequestException(
                        $"Each monthly target must equal the annual target ({annual:0.##}) for a non-cumulative KPI.");
                }
            }
        }
    }
}
