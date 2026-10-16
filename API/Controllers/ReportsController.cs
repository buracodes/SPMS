using Application.DTOS.Report;
using Application.Exceptions;
using Application.Interface.IService;
using Asp.Versioning;
using Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers;

[Route("api/reports")]
[ApiController]
[ApiVersion("1.0")]
public class ReportsController : ControllerBase
{
    private readonly IReportingService reportingService;

    public ReportsController(IReportingService _reportingService)
    {
        reportingService = _reportingService;
    }

    [HttpGet("templates")]
    [Authorize(Policy = Permissions.ReportsView)]
    public ActionResult<IReadOnlyList<ReportTemplateDto>> GetTemplates()
    {
        return Ok(reportingService.GetTemplates());
    }

    [HttpPost("generate")]
    [Authorize(Policy = Permissions.ReportsView)]
    public async Task<ActionResult<ReportEnvelopeDto>> Generate(
        [FromBody] ReportFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await reportingService.GenerateAsync(filter, ResolveRequestedBy(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("export")]
    [Authorize(Policy = Permissions.ReportsExport)]
    public async Task<IActionResult> Export(
        [FromQuery] string format,
        [FromBody] ReportFilter filter,
        CancellationToken cancellationToken)
    {
        var exportFormat = ResolveExportFormat(format);
        var bytes = await reportingService.ExportAsync(filter, exportFormat, ResolveRequestedBy(), cancellationToken);

        var (contentType, extension) = exportFormat switch
        {
            ReportExportFormat.Pdf => ("application/pdf", "pdf"),
            ReportExportFormat.Excel => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
            _ => ("text/csv", "csv")
        };

        var fileName = SanitizeFileName($"{filter.ReportType}-{DateTime.UtcNow:yyyyMMddHHmm}.{extension}");
        return File(bytes, contentType, fileName);
    }

    private static ReportExportFormat ResolveExportFormat(string? format)
    {
        switch (format?.ToLowerInvariant())
        {
            case "pdf":
                return ReportExportFormat.Pdf;
            case "xlsx":
            case "excel":
                return ReportExportFormat.Excel;
            case "csv":
                return ReportExportFormat.Csv;
            default:
                throw new BadRequestException($"Unsupported export format '{format}'.");
        }
    }

    private static string SanitizeFileName(string name)
        => name.Replace(" ", "-", StringComparison.Ordinal);

    private string? ResolveRequestedBy()
    {
        var name = User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name) ? User.Identity?.Name : name;
    }
}