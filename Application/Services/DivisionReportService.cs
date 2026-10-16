using Application.Interface.IRepository;
using Application.Interface.IService;
using ClosedXML.Excel;
using Domain.Enums;
using Domain.Entities;

namespace Application.Services;

public sealed class DivisionReportService : IDivisionReportService
{
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IKpiTargetRepository kpiTargetRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IDivisionScoreSnapshotRepository divisionSnapshotRepository;

    public DivisionReportService(
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IKpiRepository _kpiRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IDivisionScoreSnapshotRepository _divisionSnapshotRepository)
    {
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
        kpiRepository = _kpiRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        kpiTargetRepository = _kpiTargetRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        divisionSnapshotRepository = _divisionSnapshotRepository;
    }

    public async Task<IReadOnlyList<DivisionReportDto>> GetDivisionReportAsync(
        Guid divisionId,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        var division = await divisionRepository.GetById(divisionId);
        if (division == null)
        {
            return new List<DivisionReportDto>();
        }

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(fiscalYearId);

        // FR-908: scope to the requested division. Filtering only by scorecard leaked
        // every sibling division's KPIs whenever multiple divisions shared one scorecard.
        var plan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(divisionId, scorecardId);
        if (plan == null)
        {
            return new List<DivisionReportDto>();
        }

        var goals = await divisionGoalRepository.GetByPlanWithKpisAsync(plan.Id);
        var goalIds = goals.Select(g => g.Id).ToHashSet();

        var divisionKpis = kpiRepository.GetQueryable()
            .Where(k => k.DivisionGoalId != null && goalIds.Contains(k.DivisionGoalId.Value) && !k.IsDeleted)
            .ToList();

        var report = new List<DivisionReportDto>();

        foreach (var kpi in divisionKpis)
        {
            var actuals = await kpiActualValueRepository.GetForFiscalYearAsync(kpi.Id, fiscalYearId);
            var latestPerPeriod = actuals
                .GroupBy(a => a.FiscalPeriodId)
                .Select(g => g.OrderByDescending(a => a.AsOfDate).First())
                .ToList();

            var targets = await kpiTargetRepository.GetByKpiAndFiscalYearAsync(kpi.Id, fiscalYearId);

            foreach (var period in periods)
            {
                var actual = latestPerPeriod.FirstOrDefault(a => a.FiscalPeriodId == period.Id);
                // FR-908: a report row is a single fiscal month, so it must use the Monthly target.
                // Quarter-end periods also carry a Quarterly target (and the year-end period an
                // Annual target); picking without the granularity filter returned an arbitrary one.
                var target = targets.FirstOrDefault(
                    t => t.FiscalPeriodId == period.Id && t.Granularity == TargetGranularity.Monthly);

                var score = kpi.DivisionGoalId.HasValue
                    ? await divisionSnapshotRepository
                        .GetLatestAsync(kpi.DivisionGoalId.Value, period.Id, SnapshotGranularity.Month)
                    : null;

                report.Add(new DivisionReportDto(
                    divisionId,
                    division.Name,
                    scorecardId,
                    fiscalYearId,
                    period.FiscalMonth,
                    period.Quarter,
                    target?.TargetValue,
                    actual?.Value,
                    score?.Score,
                    kpi.Weight,
                    kpi.Name,
                    kpi.KpiType,
                    kpi.OptimizationType,
                    kpi.Unit,
                    kpi.IsCumulative));
            }
        }

        return report.AsReadOnly();
    }

    public async Task<byte[]> ExportDivisionActualsToExcelAsync(
        Guid divisionId,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        var report = await GetDivisionReportAsync(divisionId, scorecardId, fiscalYearId, cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Division Actuals Report");

        // Header row
        worksheet.Cell(1, 1).Value = "Division";
        worksheet.Cell(1, 2).Value = "KPI";
        worksheet.Cell(1, 3).Value = "Fiscal Month";
        worksheet.Cell(1, 4).Value = "Quarter";
        worksheet.Cell(1, 5).Value = "Target";
        worksheet.Cell(1, 6).Value = "Actual";
        worksheet.Cell(1, 7).Value = "Score";
        worksheet.Cell(1, 8).Value = "Weight %";
        worksheet.Cell(1, 9).Value = "Unit";
        worksheet.Cell(1, 10).Value = "Cumulative";

        // Data rows
        for (int i = 0; i < report.Count; i++)
        {
            var r = report[i];
            var row = i + 2;
            worksheet.Cell(row, 1).Value = r.DivisionName;
            worksheet.Cell(row, 2).Value = r.KpiName;
            worksheet.Cell(row, 3).Value = r.FiscalMonth;
            worksheet.Cell(row, 4).Value = r.Quarter;
            worksheet.Cell(row, 5).Value = r.TargetValue.HasValue ? r.TargetValue.Value.ToString("F4") : "";
            worksheet.Cell(row, 6).Value = r.ActualValue.HasValue ? r.ActualValue.Value.ToString("F4") : "";
            worksheet.Cell(row, 7).Value = r.Score.HasValue ? r.Score.Value.ToString("F4") : "";
            worksheet.Cell(row, 8).Value = r.Weight.ToString("F2") + "%";
            worksheet.Cell(row, 9).Value = r.Unit;
            worksheet.Cell(row, 10).Value = r.IsCumulative ? "Yes" : "No";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}