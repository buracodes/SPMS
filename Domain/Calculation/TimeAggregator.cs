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

    /// <summary>
    /// FR-605 windowed aggregation over fiscal months: cumulative KPIs sum the months
    /// inside the window; per-month KPIs average the months that actually carry data
    /// (partial windows stay comparable instead of diluting towards zero).
    /// </summary>
    public static decimal? Aggregate(IReadOnlyDictionary<int, decimal> monthValues, int fromMonth, int toMonth, bool isCumulative)
    {
        var window = new List<decimal?>();
        for (var m = fromMonth; m <= toMonth; m++)
        {
            window.Add(monthValues.TryGetValue(m, out var value) ? value : null);
        }

        return Aggregate(window, isCumulative);
    }
}
