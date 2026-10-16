using Domain.Common;

namespace Domain.Entities;

public class FiscalPeriod : CommonEntity
{
    public Guid FiscalYearId { get; set; }
    public FiscalYear? FiscalYear { get; set; }
    public int FiscalMonth { get; set; }
    public int Quarter { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsQuarterEnd { get; set; }
    public bool IsYearEnd { get; set; }
}
