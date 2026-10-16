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

    public string? Note { get; set; }
}
