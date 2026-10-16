namespace Domain.Models;

public class FiscalPeriodIdentifier
{
    public required string Type { get; set; }
    public required string Value { get; set; }

    public FiscalPeriodIdentifier() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FiscalPeriodIdentifier(string type, string value)
    {
        Type = type;
        Value = value;
    }

    public override string ToString() => $"{Type}:{Value}";
}