using Application.DTOS.Report;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using ClosedXML.Excel;
using Domain.Entities;
using Domain.Enums;
using Domain.Calculation;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace Application.Services;

public sealed class ReportingService : IReportingService
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKraRepository kraRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IKpiScoreSnapshotRepository kpiSnapshotRepository;
    private readonly IKraScoreSnapshotRepository kraSnapshotRepository;
    private readonly IGoalScoreSnapshotRepository goalSnapshotRepository;
    private readonly IScorecardScoreSnapshotRepository scorecardSnapshotRepository;
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeProgressCalculator initiativeProgressCalculator;
    private readonly IUserRepository userRepository;
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionReportService divisionReportService;
    private readonly IRagThresholdService ragThresholdService;

    /// <summary>Effective RAG thresholds (FR-607) for the report being generated.</summary>
    private Application.Dashboard.RagThresholds thresholds = Application.Dashboard.RagThresholds.Default;

    static ReportingService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ReportingService(
        IScorecardRepository _scorecardRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IGoalRepository _goalRepository,
        IKraRepository _kraRepository,
        IKpiRepository _kpiRepository,
        IKpiScoreSnapshotRepository _kpiSnapshotRepository,
        IKraScoreSnapshotRepository _kraSnapshotRepository,
        IGoalScoreSnapshotRepository _goalSnapshotRepository,
        IScorecardScoreSnapshotRepository _scorecardSnapshotRepository,
        IInitiativeRepository _initiativeRepository,
        IUserRepository _userRepository,
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionReportService _divisionReportService,
        IInitiativeProgressCalculator _initiativeProgressCalculator,
        IRagThresholdService _ragThresholdService)
    {
        scorecardRepository = _scorecardRepository;
        fiscalYearRepository = _fiscalYearRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        goalRepository = _goalRepository;
        kraRepository = _kraRepository;
        kpiRepository = _kpiRepository;
        kpiSnapshotRepository = _kpiSnapshotRepository;
        kraSnapshotRepository = _kraSnapshotRepository;
        goalSnapshotRepository = _goalSnapshotRepository;
        scorecardSnapshotRepository = _scorecardSnapshotRepository;
        initiativeRepository = _initiativeRepository;
        initiativeProgressCalculator = _initiativeProgressCalculator;
        userRepository = _userRepository;
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionReportService = _divisionReportService;
        ragThresholdService = _ragThresholdService;
    }

    public IReadOnlyList<ReportTemplateDto> GetTemplates()
    {
        return new List<ReportTemplateDto>
        {
            new()
            {
                Id = "scorecard-performance",
                Name = "Scorecard Performance",
                Description = "RAG-coded performance for the scorecard, goals, KRAs and KPIs by period.",
                RequiresScorecard = true
            },
            new()
            {
                Id = "kpi-detail",
                Name = "KPI Details & Trends",
                Description = "Monthly target, actual and score per KPI with RAG status.",
                RequiresScorecard = true
            },
            new()
            {
                Id = "division-performance",
                Name = "Division Performance",
                Description = "Division KPI targets, actuals, scores and RAG status by period.",
                RequiresScorecard = true
            },
            new()
            {
                Id = "initiative-status",
                Name = "Initiative Status Summary",
                Description = "Initiative progress, priorities, owners and budgets.",
                RequiresScorecard = false,
                RequiresDivision = false
            }
        };
    }

    public async Task<ReportEnvelopeDto> GenerateAsync(
        ReportFilter filter,
        string? requestedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // FR-607: band the whole report with one set of thresholds, loaded once per generation.
        thresholds = await ragThresholdService.GetEffectiveAsync(cancellationToken: cancellationToken);

        var fiscalYear = await ResolveFiscalYearAsync(filter);
        var envelope = new ReportEnvelopeDto
        {
            ReportType = filter.ReportType,
            GeneratedAtUtc = DateTime.UtcNow,
            GeneratedBy = string.IsNullOrWhiteSpace(requestedBy) ? "System" : requestedBy,
            Filters = DescribeFilters(filter, fiscalYear)
        };

        switch (filter.ReportType)
        {
            case "scorecard-performance":
                await BuildScorecardPerformanceAsync(envelope, filter, fiscalYear, cancellationToken);
                break;
            case "kpi-detail":
                await BuildKpiDetailAsync(envelope, filter, fiscalYear, cancellationToken);
                break;
            case "division-performance":
                await BuildDivisionPerformanceAsync(envelope, filter, fiscalYear, cancellationToken);
                break;
            case "initiative-status":
                await BuildInitiativeStatusAsync(envelope, filter, cancellationToken);
                break;
            default:
                throw new BadRequestException($"Unsupported report type '{filter.ReportType}'.");
        }

        return envelope;
    }

    public async Task<byte[]> ExportAsync(
        ReportFilter filter,
        ReportExportFormat format,
        string? requestedBy = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = await GenerateAsync(filter, requestedBy, cancellationToken);

        return format switch
        {
            ReportExportFormat.Excel => BuildExcel(envelope),
            ReportExportFormat.Csv => BuildCsv(envelope),
            ReportExportFormat.Pdf => BuildPdf(envelope),
            _ => throw new BadRequestException($"Unsupported export format '{format}'.")
        };
    }

    // ─────────────────────────── Builders ───────────────────────────

    private async Task BuildScorecardPerformanceAsync(
        ReportEnvelopeDto envelope, ReportFilter filter, FiscalYear? fiscalYear, CancellationToken ct)
    {
        var scorecard = await RequireScorecardAsync(filter, ct);
        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);
        var months = SelectMonths(filter, periods);
        ct.ThrowIfCancellationRequested();

        var periodByFyId = periods.ToDictionary(p => p.Id, p => p.FiscalMonth);

        var level = (filter.Level ?? "scorecard").ToLowerInvariant();
        string scorecardName = scorecard.Name;

        // Prefer month granularity snapshots.
        if (level == "kpi")
        {
            var kpiMeta = kpiRepository.GetQueryable()
                .Where(k => k.ScorecardId == scorecard.Id && !k.IsDeleted && k.IsActive)
                .Select(k => new KpiMeta(k.Id, k.Name, k.Weight, k.Unit, k.IsCumulative, k.KraId))
                .ToList();
            var kras = kraRepository.GetQueryable()
                .Where(k => k.ScorecardId == scorecard.Id && !k.IsDeleted)
                .Select(k => new KraMeta(k.Id, k.Name))
                .ToDictionary(k => k.Id, k => k.Name);
            var snaps = await kpiSnapshotRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var index = IndexByMonth(snaps, s => s.KpiId, s => periodByFyId[s.FiscalPeriodId]);

            envelope.Title = "Scorecard Performance — KPI";
            envelope.Subtitle = $"{scorecardName} · {fiscalYear?.Label}";
            envelope.Columns = new List<string> { "KPI", "KRA", "Weight %", "Period", "Target", "Actual", "Score", "RAG", "Trend" };

            foreach (var kpi in kpiMeta.OrderByDescending(k => k.Weight))
            {
                foreach (var month in months)
                {
                    var snap = GetSnapshot(index, kpi.Id, month);
                    var prev = GetPreviousSnapshot(index, kpi.Id, month);
                    envelope.Rows.Add(new List<string?>
                    {
                        kpi.Name,
                        kpi.KraId is { } kraId && kras.TryGetValue(kraId, out var kraName) ? kraName : null,
                        FormatNum(kpi.Weight),
                        MonthLabel(month),
                        FormatNum(snap?.TargetValue),
                        FormatNum(snap?.ActualValue),
                        FormatNum(snap?.Score),
                        Rag(snap?.Score),
                        Trend(prev?.Score, snap?.Score)
                    });
                }
            }

            envelope.Summary.Add("KPIs", kpiMeta.Count.ToString());
            AddLatestRagCounts(envelope, index, months, thresholds);
        }
        else if (level == "kra")
        {
            var kraMeta = kraRepository.GetQueryable()
                .Where(k => k.ScorecardId == scorecard.Id && !k.IsDeleted && k.IsActive)
                .Select(k => new KraMetaWithGoal(k.Id, k.Name, k.Weight, k.GoalId))
                .ToList();
            var goals = goalRepository.GetQueryable()
                .Where(g => g.ScorecardId == scorecard.Id && !g.IsDeleted)
                .Select(g => new { g.Id, g.Name })
                .ToDictionary(g => g.Id, g => g.Name);
            var snaps = await kraSnapshotRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var index = IndexByMonth(snaps, s => s.KraId, s => periodByFyId[s.FiscalPeriodId]);

            envelope.Title = "Scorecard Performance — KRA";
            envelope.Subtitle = $"{scorecardName} · {fiscalYear?.Label}";
            envelope.Columns = new List<string> { "KRA", "Goal", "Weight %", "Period", "Score", "RAG", "Trend" };

            foreach (var kra in kraMeta.OrderByDescending(k => k.Weight))
            {
                foreach (var month in months)
                {
                    var snap = GetSnapshot(index, kra.Id, month);
                    var prev = GetPreviousSnapshot(index, kra.Id, month);
                    envelope.Rows.Add(new List<string?>
                    {
                        kra.Name,
                        goals.TryGetValue(kra.GoalId, out var goalName) ? goalName : null,
                        FormatNum(kra.Weight),
                        MonthLabel(month),
                        FormatNum(snap?.Score),
                        Rag(snap?.Score),
                        Trend(prev?.Score, snap?.Score)
                    });
                }
            }

            envelope.Summary.Add("KRAs", kraMeta.Count.ToString());
            AddLatestRagCounts(envelope, index, months, thresholds);
        }
        else if (level == "goal")
        {
            var goalMeta = goalRepository.GetQueryable()
                .Where(g => g.ScorecardId == scorecard.Id && !g.IsDeleted && g.IsActive)
                .Select(g => new GoalMeta(g.Id, g.Name, g.Weight))
                .ToList();
            var snaps = await goalSnapshotRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var index = IndexByMonth(snaps, s => s.GoalId, s => periodByFyId[s.FiscalPeriodId]);

            envelope.Title = "Scorecard Performance — Goal";
            envelope.Subtitle = $"{scorecardName} · {fiscalYear?.Label}";
            envelope.Columns = new List<string> { "Goal", "Weight %", "Period", "Score", "RAG", "Trend" };

            foreach (var goal in goalMeta.OrderByDescending(g => g.Weight))
            {
                foreach (var month in months)
                {
                    var snap = GetSnapshot(index, goal.Id, month);
                    var prev = GetPreviousSnapshot(index, goal.Id, month);
                    envelope.Rows.Add(new List<string?>
                    {
                        goal.Name,
                        FormatNum(goal.Weight),
                        MonthLabel(month),
                        FormatNum(snap?.Score),
                        Rag(snap?.Score),
                        Trend(prev?.Score, snap?.Score)
                    });
                }
            }

            envelope.Summary.Add("Goals", goalMeta.Count.ToString());
            EnrichWithOverallSummary(envelope, index, months);
        }
        else
        {
            var snaps = await scorecardSnapshotRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
            var index = IndexByMonth(snaps, s => s.ScorecardId, s => periodByFyId[s.FiscalPeriodId]);

            envelope.Title = "Scorecard Performance";
            envelope.Subtitle = $"{scorecardName} · {fiscalYear?.Label}";
            envelope.Columns = new List<string> { "Scorecard", "Period", "Score", "RAG", "Trend" };

            foreach (var month in months)
            {
                var snap = GetSnapshot(index, scorecard.Id, month);
                var prev = GetPreviousSnapshot(index, scorecard.Id, month);
                envelope.Rows.Add(new List<string?>
                {
                    scorecardName,
                    MonthLabel(month),
                    FormatNum(snap?.Score),
                    Rag(snap?.Score),
                    Trend(prev?.Score, snap?.Score)
                });
            }

            envelope.Summary.Add("Scorecard", scorecardName);
            EnrichWithOverallSummary(envelope, index, months);
        }
    }

    private async Task BuildKpiDetailAsync(
        ReportEnvelopeDto envelope, ReportFilter filter, FiscalYear? fiscalYear, CancellationToken ct)
    {
        var scorecard = await RequireScorecardAsync(filter, ct);
        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);
        var months = SelectMonths(filter, periods);
        ct.ThrowIfCancellationRequested();

        var periodByFyId = periods.ToDictionary(p => p.Id, p => p.FiscalMonth);
        var quarterOf = CreateQuarterResolver();

        var kpiMeta = kpiRepository.GetQueryable()
            .Where(k => k.ScorecardId == scorecard.Id && !k.IsDeleted && k.IsActive)
            .Select(k => new KpiMeta(k.Id, k.Name, k.Weight, k.Unit, k.IsCumulative, k.KraId))
            .ToList();
        var kraIds = kpiMeta.Where(k => k.KraId is not null).Select(k => k.KraId!.Value).Distinct().ToList();
        var kraNameById = kraNamesById(kraIds);
        var snaps = await kpiSnapshotRepository.GetForScorecardFiscalYearAsync(scorecard.Id, scorecard.FiscalYearId);
        var index = IndexByMonth(snaps, s => s.KpiId, s => periodByFyId[s.FiscalPeriodId]);

        envelope.Title = "KPI Details & Trends";
        envelope.Subtitle = $"{scorecard.Name} · {fiscalYear?.Label}";
        envelope.Columns = new List<string>
        {
            "KPI", "KRA", "Weight %", "Unit", "Cumulative", "Period", "Qtr", "Target", "Actual", "Score", "RAG"
        };

        foreach (var kpi in kpiMeta.OrderByDescending(k => k.Weight))
        {
            foreach (var month in months)
            {
                var snap = GetSnapshot(index, kpi.Id, month);
                envelope.Rows.Add(new List<string?>
                {
                    kpi.Name,
                    kpi.KraId is { } kraId && kraNameById.TryGetValue(kraId, out var name) ? name : null,
                    FormatNum(kpi.Weight),
                    kpi.Unit,
                    kpi.IsCumulative ? "Yes" : "No",
                    MonthLabel(month),
                    quarterOf(month).ToString(),
                    FormatNum(snap?.TargetValue),
                    FormatNum(snap?.ActualValue),
                    FormatNum(snap?.Score),
                    Rag(snap?.Score)
                });
            }
        }

        envelope.Summary.Add("KPIs", kpiMeta.Count.ToString());
        envelope.Summary.Add("Months", months.Count.ToString());
        EnrichWithOverallSummary(envelope, index, months);
    }

    private async Task BuildDivisionPerformanceAsync(
        ReportEnvelopeDto envelope, ReportFilter filter, FiscalYear? fiscalYear, CancellationToken ct)
    {
        var scorecard = await RequireScorecardAsync(filter, ct);
        var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);
        var months = SelectMonths(filter, periods);
        ct.ThrowIfCancellationRequested();

        var divisions = await ResolveDivisionsAsync(filter, scorecard.Id, ct);

        envelope.Title = "Division Performance";
        envelope.Subtitle = $"{scorecard.Name} · {fiscalYear?.Label}";
        envelope.Columns = new List<string>
        {
            "Division", "KPI", "Fiscal Month", "Quarter", "Target", "Actual", "Score", "RAG", "Weight %", "Unit", "Cumulative"
        };

        foreach (var division in divisions)
        {
            var reportRows = await divisionReportService.GetDivisionReportAsync(
                division.Id, scorecard.Id, scorecard.FiscalYearId, ct);

            foreach (var row in reportRows.OrderBy(r => r.KpiName).ThenBy(r => r.FiscalMonth))
            {
                if (!months.Contains(row.FiscalMonth))
                {
                    continue;
                }

                envelope.Rows.Add(new List<string?>
                {
                    row.DivisionName,
                    row.KpiName,
                    MonthLabel(row.FiscalMonth),
                    row.Quarter.ToString(),
                    FormatNum(row.TargetValue),
                    FormatNum(row.ActualValue),
                    FormatNum(row.Score),
                    Rag(row.Score),
                    FormatNum(row.Weight),
                    row.Unit,
                    row.IsCumulative ? "Yes" : "No"
                });
            }
        }

        foreach (var division in divisions)
        {
            var rows = envelope.Rows
                .Where(r => r.Count > 0 && r[0] == division.Name)
                .ToList();
            var scores = rows
                .Select(r => r.ElementAtOrDefault(6) is { } s && decimal.TryParse(s, out var d) ? d : (decimal?)null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();

            envelope.Summary.Add(division.Name + " (KPI avg)", scores.Count == 0 ? "—" : FormatNum(scores.Average()));
        }
    }

    private async Task BuildInitiativeStatusAsync(
        ReportEnvelopeDto envelope, ReportFilter filter, CancellationToken ct)
    {
var projects = initiativeRepository.GetQueryable()
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Priority,
                i.Status,
                i.StartDate,
                i.EndDate,
                i.Budget,
                i.ActualExpenditure,
                i.IsClosed,
                i.OwnerId,
                i.GoalId,
                i.KraId,
                i.KpiId
            })
            .ToList();

        // FR-403 progress from the one authoritative calculator, fetched in a single round trip
        // rather than a per-initiative query.
        var progressInputs = projects.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<InitiativeProgressInput>>()
            : await initiativeRepository.GetProgressInputsAsync(
                projects.Select(p => p.Id).ToList(), ct);

        var userIds = projects.Select(p => p.OwnerId).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : userRepository.GetQueryable()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionary(u => u.Id, u => u.FullName);

        var goalNameById = goalNamesById(projects.Select(p => p.GoalId).ToList());
        var kraNameById = kraNamesById(projects.Select(p => p.KraId).Where(id => id is not null).Select(id => id!.Value).ToList());
        var kpiNameById = kpiNamesById(projects.Select(p => p.KpiId).Where(id => id is not null).Select(id => id!.Value).ToList());

        envelope.Title = "Initiative Status Summary";
        envelope.Subtitle = filter.ScorecardId is null ? "All initiatives" : "Initiatives across the organization";
        envelope.Columns = new List<string>
        {
            "Initiative", "Priority", "Status", "Goal", "KRA", "KPI", "Progress %", "Budget", "Owner", "Start", "End"
        };

        int[] statusCounts = new int[6];
        foreach (var project in projects.OrderBy(p => p.Status).ThenBy(p => p.Name))
        {
var progressResult = initiativeProgressCalculator.Calculate(
                progressInputs.TryGetValue(project.Id, out var inputs)
                    ? inputs
                    : Array.Empty<InitiativeProgressInput>());

            // Report the metric only when milestones exist and their weights are configured to 100.
            var progress = progressResult.HasMilestones && progressResult.WeightsAreValid
                ? (decimal?)progressResult.ProgressPercentage
                : null;

            var status = (int)project.Status;
            if (status >= 0 && status < statusCounts.Length)
            {
                statusCounts[status]++;
            }

            envelope.Rows.Add(new List<string?>
            {
                project.Name,
                project.Priority.ToString(),
                project.Status.ToString(),
                project.GoalId is { } goalId && goalNameById.TryGetValue(goalId, out var g) ? g : null,
                project.KraId is { } kraId && kraNameById.TryGetValue(kraId, out var k) ? k : null,
                project.KpiId is { } kpiId && kpiNameById.TryGetValue(kpiId, out var kp) ? kp : null,
                progress is null ? null : FormatNum(progress.Value),
                project.Budget == 0 ? null : FormatNum(project.Budget),
                users.TryGetValue(project.OwnerId, out var owner) ? owner : null,
                project.StartDate.ToString("yyyy-MM-dd"),
                project.EndDate.ToString("yyyy-MM-dd")
            });
        }

        envelope.Summary.Add("Total initiatives", projects.Count.ToString());
        envelope.Summary.Add("Not started", statusCounts[(int)InitiativeStatus.NotStarted].ToString());
        envelope.Summary.Add("In progress", statusCounts[(int)InitiativeStatus.InProgress].ToString());
        envelope.Summary.Add("Completed", statusCounts[(int)InitiativeStatus.Completed].ToString());
    }

    // ─────────────────────────── Exporters ───────────────────────────

    private static byte[] BuildExcel(ReportEnvelopeDto envelope)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Report");

        int colCount = Math.Max(envelope.Columns.Count, 1);

        ws.Cell(1, 1).Value = envelope.Title;
        ws.Range(1, 1, 1, colCount).Merge();
        ws.Cell(1, 1).Style.Font.SetBold();
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        if (!string.IsNullOrWhiteSpace(envelope.Subtitle))
        {
            ws.Cell(2, 1).Value = envelope.Subtitle;
            ws.Range(2, 1, 2, colCount).Merge();
        }

        ws.Cell(3, 1).Value = $"Generated {envelope.GeneratedAtUtc:yyyy-MM-dd HH:mm 'UTC'} by {envelope.GeneratedBy}";
        ws.Range(3, 1, 3, colCount).Merge();

        if (envelope.Filters.Count > 0)
        {
            ws.Cell(4, 1).Value = "Filters: " + string.Join(", ", envelope.Filters.Select(f => $"{f.Key}={f.Value}"));
            ws.Range(4, 1, 4, colCount).Merge();
        }

        int headerRow = envelope.Summary.Count > 0 ? 6 : 5;
        if (envelope.Summary.Count > 0)
        {
            int s = 5;
            foreach (var kv in envelope.Summary)
            {
                ws.Cell(s, 1).Value = kv.Key;
                ws.Cell(s, 2).Value = kv.Value ?? string.Empty;
                ws.Cell(s, 1).Style.Font.SetBold();
                ws.Cell(s, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EFF6FF"));
                ws.Cell(s, 2).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F8FAFC"));
                s++;
            }
        }

        for (int c = 0; c < envelope.Columns.Count; c++)
        {
            ws.Cell(headerRow, c + 1).Value = envelope.Columns[c];
            ws.Cell(headerRow, c + 1).Style.Font.SetBold();
            ws.Cell(headerRow, c + 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#18552C"));
            ws.Cell(headerRow, c + 1).Style.Font.SetFontColor(XLColor.White);
        }

        for (int r = 0; r < envelope.Rows.Count; r++)
        {
            var row = envelope.Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                ws.Cell(headerRow + 1 + r, c + 1).Value = row[c] ?? string.Empty;
            }
        }

        if (envelope.Rows.Count > 0)
        {
            ws.Range(headerRow, 1, headerRow + envelope.Rows.Count, colCount).SetAutoFilter();
            ws.SheetView.FreezeRows(headerRow);
        }

        ws.Columns().AdjustToContents(10, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildCsv(ReportEnvelopeDto envelope)
    {
        var sb = new StringBuilder();
        foreach (var kv in envelope.Filters)
        {
            sb.Append('#').Append(kv.Key).Append(',').AppendLine(EscapeCsv(kv.Value ?? string.Empty));
        }
        sb.AppendLine();

        sb.AppendLine(string.Join(",", envelope.Columns.Select(EscapeCsv)));
        foreach (var row in envelope.Rows)
        {
            while (row.Count < envelope.Columns.Count)
            {
                row.Add(null);
            }
            sb.AppendLine(string.Join(",", row.Select(c => EscapeCsv(c ?? string.Empty))));
        }

        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static byte[] BuildPdf(ReportEnvelopeDto envelope)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor("#111827"));

                page.Header().Column(col =>
                {
                    col.Item().Text(envelope.Title).FontSize(18).Bold().FontColor("#18552C");
                    if (!string.IsNullOrWhiteSpace(envelope.Subtitle))
                    {
                        col.Item().Text(envelope.Subtitle).FontSize(12).FontColor("#475569");
                    }
                    col.Item().Text($"Generated {envelope.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC · {envelope.GeneratedBy}")
                        .FontSize(9).FontColor("#94A3B8");
                    if (envelope.Summary.Count > 0)
                    {
                        col.Item().PaddingTop(6).Row(row =>
                        {
                            var cells = envelope.Summary.ToList();
                            for (int i = 0; i < cells.Count; i++)
                            {
                                row.RelativeItem().Text($"{cells[i].Key}: {cells[i].Value}").FontSize(9);
                            }
                        });
                    }
                });

                page.Content().PaddingVertical(12).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        for (int i = 0; i < Math.Max(envelope.Columns.Count, 1); i++)
                        {
                            cols.RelativeColumn(1);
                        }
                    });

                    table.Header(header =>
                    {
                        foreach (var col in envelope.Columns)
                        {
                            header.Cell().Background("#18552C")
                                .Text(col).FontColor(Colors.White).Bold().FontSize(9);
                        }
                    });

                    foreach (var row in envelope.Rows)
                    {
                        for (int c = 0; c < Math.Max(envelope.Columns.Count, 1); c++)
                        {
                            var text = c < row.Count ? row[c] ?? string.Empty : string.Empty;
                            var cell = table.Cell();

                            var color = text switch
                            {
                                "Green" => "#16A34A",
                                "Yellow" => "#D97706",
                                "Amber" => "#D97706",
                                "Red" => "#DC2626",
                                "Gray" => "#6B7280",
                                _ => "#111827"
                            };

                            cell.Text(text).FontColor(color).FontSize(8);
                        }
                    }
                });

                page.Footer().AlignCenter().Text("SPMS — Strategic Performance Management System").FontSize(8).FontColor("#94A3B8");
            });
        }).GeneratePdf();
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private async Task<Scorecard> RequireScorecardAsync(ReportFilter filter, CancellationToken ct)
    {
        if (filter.ScorecardId is not { } scorecardId)
        {
            throw new BadRequestException("A scorecard must be selected for this report.");
        }

        var scorecard = await scorecardRepository.GetById(scorecardId);
        if (scorecard is null)
        {
            throw new NotFoundException(nameof(Scorecard), scorecardId);
        }

        if (filter.FiscalYearId is { } fyId && scorecard.FiscalYearId != fyId)
        {
            throw new BadRequestException("The selected fiscal year does not match the scorecard's fiscal year.");
        }

        return scorecard;
    }

    private async Task<FiscalYear?> ResolveFiscalYearAsync(ReportFilter filter)
    {
        if (filter.FiscalYearId is not { } id)
        {
            return null;
        }

        return await fiscalYearRepository.GetById(id);
    }

    private async Task<IReadOnlyList<Division>> ResolveDivisionsAsync(
        ReportFilter filter, Guid scorecardId, CancellationToken ct)
    {
        if (filter.DivisionId is { } divisionId)
        {
            var division = await divisionRepository.GetById(divisionId);
            return division is null ? Array.Empty<Division>() : new[] { division };
        }

        var plans = await divisionPlanRepository.GetByScorecardAsync(scorecardId);
        var divisionIds = plans.Select(p => p.DivisionId).Distinct().ToList();
        if (divisionIds.Count == 0)
        {
            return Array.Empty<Division>();
        }

        var all = await divisionRepository.GetActiveAsync();
        return all.Where(d => divisionIds.Contains(d.Id)).ToList();
    }

    private static List<int> SelectMonths(ReportFilter filter, IReadOnlyList<FiscalPeriod> periods)
    {
        var months = periods.Select(p => p.FiscalMonth)
            .Where(m => m >= 1 && m <= 12)
            .OrderBy(m => m)
            .ToList();

        if (filter.FromMonth is { } from)
        {
            months = months.Where(m => m >= from).ToList();
        }

        if (filter.ToMonth is { } to)
        {
            months = months.Where(m => m <= to).ToList();
        }

        if (filter.Quarter is { } quarter && quarter >= 1 && quarter <= 4)
        {
            months = months.Where(m => ((m - 1) / 3) + 1 == quarter).ToList();
        }

        switch (filter.Granularity?.ToLowerInvariant())
        {
            case "quarter":
                // A quarter is already pinned via Quarter/from-to; otherwise fall back to quarter-end months.
                if (filter.Quarter is null && filter.FromMonth is null && filter.ToMonth is null)
                {
                    months = months.Where(m => m % 3 == 0).ToList();
                }
                break;
            case "year":
                if (months.Count > 0)
                {
                    months = new List<int> { months.Max() };
                }
                break;
            default:
                break;
        }

        return months;
    }

    private static Func<int, int> CreateQuarterResolver()
        => m => ((m - 1) / 3) + 1;

    private static Dictionary<Guid, Dictionary<int, S>> IndexByMonth<S>(
        IEnumerable<S> items,
        Func<S, Guid> entityId,
        Func<S, int> month)
    {
        var result = new Dictionary<Guid, Dictionary<int, S>>();

        foreach (var item in items)
        {
            var key = entityId(item);
            var m = month(item);

            if (!result.TryGetValue(key, out var inner))
            {
                inner = new Dictionary<int, S>();
                result[key] = inner;
            }

            // First write wins; callers order snapshots by granularity then CalculatedAt desc.
            if (!inner.ContainsKey(m))
            {
                inner[m] = item;
            }
        }

        return result;
    }

    private static S? GetSnapshot<S>(Dictionary<Guid, Dictionary<int, S>> index, Guid entityId, int month)
    {
        return index.TryGetValue(entityId, out var inner) && inner.TryGetValue(month, out var snap)
            ? snap
            : default;
    }

    private static S? GetPreviousSnapshot<S>(Dictionary<Guid, Dictionary<int, S>> index, Guid entityId, int month)
        where S : IScoreSnapshot
    {
        var previousMonth = month == 1 ? 12 : month - 1;
        return GetSnapshot(index, entityId, previousMonth);
    }

    private Dictionary<Guid, string> kraNamesById(IReadOnlyList<Guid> ids)
    {
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : kraRepository.GetQueryable()
                .Where(k => ids.Contains(k.Id))
                .Select(k => new { k.Id, k.Name })
                .ToDictionary(k => k.Id, k => k.Name);
    }

    private Dictionary<Guid, string> goalNamesById(IReadOnlyList<Guid?> ids)
    {
        var concrete = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        return concrete.Count == 0
            ? new Dictionary<Guid, string>()
            : goalRepository.GetQueryable()
                .Where(g => concrete.Contains(g.Id))
                .Select(g => new { g.Id, g.Name })
                .ToDictionary(g => g.Id, g => g.Name);
    }

    private Dictionary<Guid, string> kpiNamesById(IReadOnlyList<Guid> ids)
    {
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : kpiRepository.GetQueryable()
                .Where(k => ids.Contains(k.Id))
                .Select(k => new { k.Id, k.Name })
                .ToDictionary(k => k.Id, k => k.Name);
    }

    private Dictionary<string, string?> DescribeFilters(ReportFilter filter, FiscalYear? fiscalYear)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Report type"] = filter.ReportType,
            ["Granularity"] = (filter.Granularity ?? "month").ToLowerInvariant()
        };

        if (filter.FiscalYearId is not null)
        {
            dict["Fiscal year"] = fiscalYear?.Label ?? "—";
        }

        if (filter.ScorecardId is not null)
        {
            dict["Scorecard"] = filter.ScorecardId.Value.ToString("D");
        }

        if (filter.DivisionId is not null)
        {
            dict["Division"] = filter.DivisionId.Value.ToString("D");
        }

        if (filter.Level is not null)
        {
            dict["Level"] = filter.Level;
        }

        if (filter.FromMonth is not null)
        {
            dict["From"] = "M" + filter.FromMonth;
        }

        if (filter.ToMonth is not null)
        {
            dict["To"] = "M" + filter.ToMonth;
        }

        if (filter.Quarter is { } quarter && quarter >= 1 && quarter <= 4)
        {
            dict["Quarter"] = "Q" + quarter;
        }

        return dict;
    }

    private static void EnrichWithOverallSummary<S>(
        ReportEnvelopeDto envelope, Dictionary<Guid, Dictionary<int, S>> index, List<int> months)
        where S : IScoreSnapshot
    {
        if (months.Count == 0)
        {
            return;
        }

        var latestMonth = months.Max();
        var scores = new List<decimal?>();
        foreach (var inner in index.Values)
        {
            if (inner.TryGetValue(latestMonth, out var snap))
            {
                scores.Add(snap.Score);
            }
        }

        var valid = scores.Where(s => s.HasValue).Select(s => s!.Value).ToList();

        if (valid.Count > 0)
        {
            envelope.Summary["Overall score (M" + latestMonth + ")"] = FormatNum(valid.Average());
        }
    }

    private static void AddLatestRagCounts<S>(
        ReportEnvelopeDto envelope, Dictionary<Guid, Dictionary<int, S>> index, List<int> months,
        Application.Dashboard.RagThresholds thresholds)
        where S : IScoreSnapshot
    {
        if (months.Count == 0)
        {
            return;
        }

        var latestMonth = months.Max();
        int green = 0;
        int yellow = 0;
        int red = 0;
        int none = 0;

        foreach (var inner in index.Values)
        {
            if (!inner.TryGetValue(latestMonth, out var snap) || snap.Score is not { } s)
            {
                none++;
                continue;
            }

            switch (thresholds.Classify(s))
            {
                case Dashboard.DashboardRag.Green:
                    green++;
                    break;
                case Dashboard.DashboardRag.Amber:
                    yellow++;
                    break;
                default:
                    red++;
                    break;
            }
        }

        if (green + yellow + red > 0)
        {
            envelope.Summary["Green (M" + latestMonth + ")"] = green.ToString();
            envelope.Summary["Amber (M" + latestMonth + ")"] = yellow.ToString();
            envelope.Summary["Red (M" + latestMonth + ")"] = red.ToString();
        }
    }

    private string? Rag(decimal? score)
    {
        // One authoritative band: configured thresholds loaded for this report, evaluated by
        // DashboardRag, so the dashboard and the report engine can never disagree.
        return score is null ? null : Application.Dashboard.DashboardRag.FromScore(score, thresholds);
    }

    private static string? Trend(decimal? previous, decimal? current)
    {
        if (previous is not { } p || current is not { } c)
        {
            return null;
        }

        var delta = c - p;
        var sign = delta > 0 ? "+" : delta < 0 ? "-" : "";
        return sign + Math.Abs(delta).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string MonthLabel(int fiscalMonth)
        => "M" + fiscalMonth;

    private static string? FormatNum(decimal? value)
    {
        if (value is not { } v)
        {
            return null;
        }

        return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ─────────────────────────── Meta records ───────────────────────────

    private sealed record KpiMeta(Guid Id, string Name, decimal Weight, string? Unit, bool IsCumulative, Guid? KraId);
    private sealed record KraMeta(Guid Id, string Name);
    private sealed record KraMetaWithGoal(Guid Id, string Name, decimal Weight, Guid GoalId);
    private sealed record GoalMeta(Guid Id, string Name, decimal Weight);
}