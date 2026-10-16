namespace Domain.Calculation;

public static class TimeAggregator
{
    public static decimal? Aggregate(IReadOnlyList<decimal?> periodValues, bool isCumulative)
    {
        var values = periodValues.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return isCumulative ? values.Sum() : values.Average();
    }
}
