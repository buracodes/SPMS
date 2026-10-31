using Application.Interface.IService;
using Domain.Enums;
using Domain.Entities;

namespace Application.Interface.IService;

public sealed record DivisionReportDto(
    Guid DivisionId,
    string DivisionName,
    string GoalName,
    string? CorporateGoalName,
    Guid ScorecardId,
    Guid FiscalYearId,
    int FiscalMonth,
    int Quarter,
    decimal? TargetValue,
    decimal? ActualValue,
    decimal? Score,
    decimal Weight,
    string KpiName,
    KpiType KpiType,
    KpiOptimizationType OptimizationType,
    string? Unit,
    bool IsCumulative
);

public interface IDivisionReportService
{
    Task<IReadOnlyList<DivisionReportDto>> GetDivisionReportAsync(
        Guid divisionId,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the per-KPI report rows for several divisions in a fixed number of queries
    /// (plans, goals/KPIs, actuals, targets and snapshots are each batch-loaded), instead of
    /// issuing three queries per KPI per division.
    /// </summary>
    Task<IReadOnlyList<DivisionReportDto>> GetDivisionReportsAsync(
        IReadOnlyList<Guid> divisionIds,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<byte[]> ExportDivisionActualsToExcelAsync(
        Guid divisionId,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default);
}