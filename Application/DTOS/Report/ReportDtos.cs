namespace Application.DTOS.Report;

public sealed class ReportTemplateDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public bool RequiresScorecard { get; set; }
    public bool RequiresDivision { get; set; }
}

public sealed class ReportFilter
{
    public required string ReportType { get; set; }

    public Guid? ScorecardId { get; set; }

    public Guid? FiscalYearId { get; set; }

    public Guid? DivisionId { get; set; }

    /// <summary>scorecard | goal | kra | kpi</summary>
    public string? Level { get; set; }

    /// <summary>month | quarter | year | ytd</summary>
    public string? Granularity { get; set; }

    public int? FromMonth { get; set; }

    public int? ToMonth { get; set; }

    public int? Quarter { get; set; }
}

public sealed class ReportEnvelopeDto
{
    public required string ReportType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string? GeneratedBy { get; set; }
    public Dictionary<string, string?> Filters { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public List<List<string?>> Rows { get; set; } = new();
    public Dictionary<string, string?> Summary { get; set; } = new();
}

public enum ReportExportFormat
{
    Pdf = 1,
    Excel = 2,
    Csv = 3
}