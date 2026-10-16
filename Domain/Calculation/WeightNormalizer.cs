namespace Domain.Calculation;

public static class WeightNormalizer
{
    public static decimal? WeightedAverage(IReadOnlyList<(decimal Score, decimal Weight)> contributions)
    {
        var items = contributions.Where(c => c.Weight > 0m).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        var totalWeight = items.Sum(c => c.Weight);
        if (totalWeight <= 0m)
        {
            return null;
        }

        return items.Sum(c => c.Score * c.Weight) / totalWeight;
    }
}
