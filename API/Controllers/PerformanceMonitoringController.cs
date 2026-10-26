using Application.DTOS.PerformanceMonitoring;
using Application.Interface.IService;
using Asp.Versioning;
using Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class PerformanceMonitoringController : ControllerBase
{
    private readonly IPerformanceMonitoringService service;

    public PerformanceMonitoringController(IPerformanceMonitoringService service)
    {
        this.service = service;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<PerformanceMonitoringDto>> Get(
        [FromQuery] Guid? fiscalYearId,
        [FromQuery] Guid? scorecardId,
        [FromQuery] Guid? divisionId,
        [FromQuery] string? periodType,
        [FromQuery] int? quarter,
        [FromQuery] int? fromMonth,
        [FromQuery] int? toMonth,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(new PerformanceMonitoringFilter
        {
            FiscalYearId = fiscalYearId,
            ScorecardId = scorecardId,
            DivisionId = divisionId,
            PeriodType = periodType,
            Quarter = quarter,
            FromMonth = fromMonth,
            ToMonth = toMonth,
            Year = year
        }, cancellationToken);

        return Ok(result);
    }
}
