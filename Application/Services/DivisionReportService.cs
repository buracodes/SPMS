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
    private readonly IKpiScoreSnapshotRepository kpiSnapshotRepository;

    public DivisionReportService(
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IKpiRepository _kpiRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IKpiTargetRepository _kpiTargetRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IKpiScoreSnapshotRepository _kpiSnapshotRepository)
    {
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
        kpiRepository = _kpiRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        kpiTargetRepository = _kpiTargetRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        kpiSnapshotRepository = _kpiSnapshotRepository;
    }

    public Task<IReadOnlyList<DivisionReportDto>> GetDivisionReportAsync(
        Guid divisionId,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
        => GetDivisionReportsAsync(new[] { divisionId }, scorecardId, fiscalYearId, cancellationToken);

    public async Task<IReadOnlyList<DivisionReportDto>> GetDivisionReportsAsync(
        IReadOnlyList<Guid> divisionIds,
        Guid scorecardId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (divisionIds.Count == 0)
        {
            return Array.Empty<DivisionReportDto>();
        }

        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(fiscalYearId);
        if (periods.Count == 0)
        {
            return Array.Empty<DivisionReportDto>();
        }

        var divisionNameById = divisionRepository.GetQueryable()
            .Where(d => divisionIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToDictionary(d => d.Id, d => d.Name);
        if (divisionNameById.Count == 0)
        {
            return Array.Empty<DivisionReportDto>();
        }

        // FR-908: scope to the requested divisions. Filtering only by scorecard leaked
        // every sibling division's KPIs whenever multiple divisions shared one scorecard.
        var plans = await divisionPlanRepository.GetByScorecardAsync(scorecardId);
        var plansByDivision = plans
            .Where(p => divisionIds.Contains(p.DivisionId))
            .GroupBy(p => p.DivisionId)
            .ToDictionary(g => g.Key, g => g.First());
        if (plansByDivision.Count == 0)
        {
            return Array.Empty<DivisionReportDto>();
        }

        // Batch-load the whole report in a fixed number of queries. The previous version
        // issued five queries per division plus three per KPI, which made a corporate-wide
        // report an N+1 storm.
        var goals = await divisionGoalRepository.GetByPlanIdsWithKpisAsync(
            plansByDivision.Values.Select(p => p.Id).ToList());
        var goalById = goals.ToDictionary(g => g.Id);
        var planById = plansByDivision.Values.ToDictionary(p => p.Id);

        var goalIds = goalById.Keys.ToHashSet();
        var divisionKpis = kpiRepository.GetQueryable()
            .Where(k => k.DivisionGoalId != null && goalIds.Contains(k.DivisionGoalId.Value) && !k.IsDeleted)
            .ToList();
        var kpiIds = divisionKpis.Select(k => k.Id).ToHashSet();

        var actuals = await kpiActualValueRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);
        var latestActualByKpiPeriod = actuals
            .Where(a => kpiIds.Contains(a.KpiId))
            .GroupBy(a => (a.KpiId, a.FiscalPeriodId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AsOfDate).First());

        var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);
        var monthlyTargetByKpiPeriod = targets
            .Where(t => kpiIds.Contains(t.KpiId) && t.Granularity == TargetGranularity.Monthly)
            .GroupBy(t => (t.KpiId, t.FiscalPeriodId))
            .ToDictionary(g => g.Key, g => g.First());

        // FR-908: the row is a single KPI, so its score must come from that KPI's snapshot.
        // Reading the parent goal's division snapshot put the same rolled-up goal score on
        // every KPI row of the goal.
        var snapshots = await kpiSnapshotRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);
        var monthlyScoreByKpiPeriod = snapshots
            .Where(s => kpiIds.Contains(s.KpiId) && s.Granularity == SnapshotGranularity.Month)
            .GroupBy(s => (s.KpiId, s.FiscalPeriodId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CalculatedAt).First());

        var report = new List<DivisionReportDto>();

        foreach (var kpi in divisionKpis)
        {
            var goal = goalById[kpi.DivisionGoalId!.Value];
            var plan = planById[goal.DivisionPlanId];
            if (!divisionNameById.TryGetValue(plan.DivisionId, out var divisionName))
            {
                continue;
            }

            foreach (var period in periods)
            {
                latestActualByKpiPeriod.TryGetValue((kpi.Id, period.Id), out var actual);
                monthlyTargetByKpiPeriod.TryGetValue((kpi.Id, period.Id), out var target);
                monthlyScoreByKpiPeriod.TryGetValue((kpi.Id, period.Id), out var score);

                report.Add(new DivisionReportDto(
                    plan.DivisionId,
                    divisionName,
                    goal.Name,
                    goal.ParentGoal?.Name,
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

        // Header row — FR-909 requires goals, KPIs, targets, actuals, scores and alignment.
        worksheet.Cell(1, 1).Value = "Division";
        worksheet.Cell(1, 2).Value = "Division Goal";
        worksheet.Cell(1, 3).Value = "Corporate Goal";
        worksheet.Cell(1, 4).Value = "KPI";
        worksheet.Cell(1, 5).Value = "Fiscal Month";
        worksheet.Cell(1, 6).Value = "Quarter";
        worksheet.Cell(1, 7).Value = "Target";
        worksheet.Cell(1, 8).Value = "Actual";
        worksheet.Cell(1, 9).Value = "Score";
        worksheet.Cell(1, 10).Value = "Weight %";
        worksheet.Cell(1, 11).Value = "Unit";
        worksheet.Cell(1, 12).Value = "Cumulative";

        // Data rows
        for (int i = 0; i < report.Count; i++)
        {
            var r = report[i];
            var row = i + 2;
            worksheet.Cell(row, 1).Value = r.DivisionName;
            worksheet.Cell(row, 2).Value = r.GoalName;
            worksheet.Cell(row, 3).Value = r.CorporateGoalName;
            worksheet.Cell(row, 4).Value = r.KpiName;
            worksheet.Cell(row, 5).Value = r.FiscalMonth;
            worksheet.Cell(row, 6).Value = r.Quarter;
            worksheet.Cell(row, 7).Value = r.TargetValue.HasValue ? r.TargetValue.Value.ToString("F4") : "";
            worksheet.Cell(row, 8).Value = r.ActualValue.HasValue ? r.ActualValue.Value.ToString("F4") : "";
            worksheet.Cell(row, 9).Value = r.Score.HasValue ? r.Score.Value.ToString("F4") : "";
            worksheet.Cell(row, 10).Value = r.Weight.ToString("F2") + "%";
            worksheet.Cell(row, 11).Value = r.Unit;
            worksheet.Cell(row, 12).Value = r.IsCumulative ? "Yes" : "No";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}