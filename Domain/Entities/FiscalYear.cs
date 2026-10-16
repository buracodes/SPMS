using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class FiscalYear : CommonEntity
{
    public required string Label { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public FiscalYearStatus FiscalYearStatus { get; set; } = FiscalYearStatus.Open;
    public ICollection<FiscalPeriod> Periods { get; set; } = new List<FiscalPeriod>();
    public ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();
}
