using Domain.Models;

namespace Domain.Factories;

public interface IFiscalPeriodIdentifierFactory
{
    FiscalPeriodIdentifier Create(string periodType, string value);
    FiscalPeriodIdentifier Parse(string input);
}

public class FiscalPeriodIdentifierFactory : IFiscalPeriodIdentifierFactory
{
    private static readonly HashSet<string> ValidTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ytd", "quarter", "month", "year"
    };

    public FiscalPeriodIdentifier Create(string periodType, string value)
    {
        var normalizedType = periodType?.ToLowerInvariant() ?? "ytd";
        if (!ValidTypes.Contains(normalizedType))
            throw new ArgumentException($"Invalid period type: {periodType}", nameof(periodType));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty", nameof(value));

        return new FiscalPeriodIdentifier(normalizedType, value);
    }

    public FiscalPeriodIdentifier Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be empty", nameof(input));

        var parts = input.Split(':', 2);
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid period identifier format: {input}. Expected format: 'type:value'");

        return Create(parts[0], parts[1]);
    }
}