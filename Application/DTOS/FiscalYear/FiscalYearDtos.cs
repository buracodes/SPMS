namespace Application.DTOS.FiscalYear;

public class FiscalYearDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// True when at least one scorecard was built on this year. Once that happens the year's
    /// dates become read-only (the backend enforces it; the UI uses this to hide the affordance).
    /// </summary>
    public bool HasScorecards { get; set; }
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

    /// <summary>
    /// Optional new start date (the first day of a month). The end date is never accepted from
    /// the client: the server always derives it as the last day of the twelfth month, so the
    /// twelve-month span can never drift or be edited independently.
    /// </summary>
    public DateTime? StartDate { get; set; }
}
