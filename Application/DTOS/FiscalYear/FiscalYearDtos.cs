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
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class UpdateFiscalYearDto
{
    public string? Label { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
