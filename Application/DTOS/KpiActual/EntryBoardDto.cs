using Application.DTOS.Kpi;
using Application.DTOS.KpiTarget;
using Domain.Enums;

namespace Application.DTOS.KpiActual;

/// <summary>
/// Everything the Data Entry page needs for one fiscal year, resolved against the signed-in
/// principal's entry scope: the KPIs they report on, their monthly targets and the actual
/// values already recorded, plus the scorecards those KPIs sit on.
/// </summary>
public class EntryBoardDto
{
    public Guid FiscalYearId { get; set; }

    public List<EntryScorecardDto> Scorecards { get; set; } = new();

    /// <summary>KPI rows in entry scope, using the same contract as the KPI grid.</summary>
    public List<GetAllKpiDto> Kpis { get; set; } = new();

    public List<KpiTargetsBundleDto> Targets { get; set; } = new();

    public List<GetActualValuesDto> Actuals { get; set; } = new();
}

/// <summary>
/// A scorecard that holds at least one KPI in entry scope. Only the fields the entry picker
/// shows are returned; the corporate scorecard catalog stays behind its own read right.
/// </summary>
public class EntryScorecardDto
{
    public Guid Id { get; set; }
    public Guid FiscalYearId { get; set; }
    public required string Name { get; set; }
    public ScorecardStatus ScorecardStatus { get; set; }
}
