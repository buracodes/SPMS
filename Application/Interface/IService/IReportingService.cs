using Application.DTOS.Report;

namespace Application.Interface.IService;

public interface IReportingService
{
    IReadOnlyList<ReportTemplateDto> GetTemplates();

    Task<ReportEnvelopeDto> GenerateAsync(
        ReportFilter filter,
        string? requestedBy = null,
        CancellationToken cancellationToken = default);

    Task<byte[]> ExportAsync(
        ReportFilter filter,
        ReportExportFormat format,
        string? requestedBy = null,
        CancellationToken cancellationToken = default);
}