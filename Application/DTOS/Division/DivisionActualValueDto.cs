using Domain.Enums;

namespace Application.DTOS.Division;

public class DivisionActualValueDto
{
    public Guid Id { get; set; }

    public Guid DivisionId { get; set; }

    public Guid KpiId { get; set; }

    public Guid FiscalPeriodId { get; set; }

    public int FiscalMonth { get; set; }

    public int Quarter { get; set; }

    public decimal Value { get; set; }

    public KpiDataSource DataSource { get; set; }

    public DateTime AsOfDate { get; set; }

    public bool IsCurrent { get; set; }

    /// <summary>The previous revision superseded by this revision, when supplied.</summary>
    public Guid? SupersedesActualId { get; set; }

    public string? Note { get; set; }

    /// <summary>Attribution of the original entry, from the authenticated principal.</summary>
    public Guid? RecordedBy { get; set; }

    /// <summary>Display name resolved in a single batched lookup for the result set.</summary>
    public string? EnteredByName { get; set; }

    /// <summary>Timestamp the revision was entered (server clock).</summary>
    public DateTime CreatedDate { get; set; }
}
