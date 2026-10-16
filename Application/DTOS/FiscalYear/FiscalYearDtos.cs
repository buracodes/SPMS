namespace Application.DTOS.FiscalYear;

public class FiscalYearDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateFiscalYearDto
{
    public required string Label { get; set; }

    /// <summary>The first day of the first fiscal month; the server rejects any other day.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Optional. When supplied it must equal the server-computed end date (twelve consecutive
    /// months after <see cref="StartDate"/>); otherwise the server derives it. Clients may send
    /// nothing and let the server calculate the authoritative value.
    /// </summary>
    public DateTime? EndDate { get; set; }
}

public class UpdateFiscalYearDto
{
    public string? Label { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
