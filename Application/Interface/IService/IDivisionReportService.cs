using Application.Interface.IService;
using Domain.Enums;
using Domain.Entities;

namespace Application.Interface.IService;

public sealed record DivisionReportDto(
    Guid DivisionId,
    string DivisionName,
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

    Task<byte[]> ExportDivisionActualsToExcelAsync(
        Guid divisionId,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default);
}